using System.Net;
using System.Text;
using CseForms.Application.BackOffice;
using Microsoft.EntityFrameworkCore;

namespace CseForms.Api.Tests;

/// <summary>Traitement des réponses dans le back-office : statut, historique, export CSV, suppression.</summary>
public partial class BackOfficeTests
{
    private async Task<HttpClient> LoggedInAsync(string role, string? grantSlug = null)
    {
        var email = await CreateUserAsync(role);
        if (grantSlug is not null)
        {
            Assert.Equal(0, await RunCliAsync(string.Empty, "grant", "--email", email, "--form", grantSlug));
        }

        var client = Client();
        await LoginAsync(client, email, Password);
        return client;
    }

    private static FormUrlEncodedContent Post(string page, params (string Key, string Value)[] fields) =>
        new(fields.Select(f => KeyValuePair.Create(f.Key, f.Value))
            .Append(KeyValuePair.Create("__RequestVerificationToken", Token(page))));

    [Fact]
    public async Task Editor_changes_status_which_schedules_purge_and_appears_in_history()
    {
        var (form, submissionId) = await SeedSubmissionAsync("bo-statut");
        await using (var db = factory.Database.CreateContext())
        {
            (await db.Forms.SingleAsync(f => f.Id == form.Id)).SetRetentionDays(180);
            await db.SaveChangesAsync();
        }

        var client = await LoggedInAsync(AppRoles.Editor, "bo-statut");
        var page = await client.GetStringAsync($"/admin/reponses/{submissionId}");
        Assert.Contains("Enregistrer le statut", page, StringComparison.Ordinal);
        Assert.Contains("180 jours à partir du traitement", page, StringComparison.Ordinal);

        var response = await client.PostAsync($"/admin/reponses/{submissionId}?handler=Status", Post(page, ("status", "entered")));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/admin/reponses/{submissionId}?statut=modifie", response.Headers.Location!.OriginalString);

        var after = await client.GetStringAsync(response.Headers.Location.OriginalString);
        Assert.Contains("Statut enregistré : Saisie dans le logiciel CSE", after, StringComparison.Ordinal);
        Assert.Contains("Suppression prévue le", after, StringComparison.Ordinal);
        Assert.Contains("Statut changé : « Nouvelle » vers « Saisie dans le logiciel CSE »", after, StringComparison.Ordinal);
        Assert.Contains("par Élu fictif", after, StringComparison.Ordinal);

        await using (var db = factory.Database.CreateContext())
        {
            var submission = await db.Submissions.SingleAsync(s => s.Id == submissionId);
            Assert.Equal("entered", submission.Status);
            Assert.NotNull(submission.PurgeAt);
            Assert.Equal("new>entered", (await db.AuditLog.SingleAsync(a => a.Target == $"submission:{submissionId}" && a.Action == AuditActions.SubmissionStatusChanged)).Detail);
        }
    }

    [Fact]
    public async Task Status_change_rejects_unknown_status_and_missing_token()
    {
        var (_, submissionId) = await SeedSubmissionAsync("bo-statut-invalide");
        var client = await LoggedInAsync(AppRoles.Admin);
        var page = await client.GetStringAsync($"/admin/reponses/{submissionId}");

        var unknown = await client.PostAsync($"/admin/reponses/{submissionId}?handler=Status", Post(page, ("status", "supprimee")));
        var noToken = await client.PostAsync(
            $"/admin/reponses/{submissionId}?handler=Status",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["status"] = "entered" }));

        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        await using var db = factory.Database.CreateContext();
        Assert.Equal("new", (await db.Submissions.SingleAsync(s => s.Id == submissionId)).Status);
    }

    [Fact]
    public async Task Reader_sees_history_but_cannot_change_status_export_or_delete()
    {
        var (form, submissionId) = await SeedSubmissionAsync("bo-lecteur");
        var client = await LoggedInAsync(AppRoles.Reader, "bo-lecteur");

        var page = await client.GetStringAsync($"/admin/reponses/{submissionId}");
        Assert.Contains("Historique", page, StringComparison.Ordinal);
        Assert.Contains("Réponse reçue", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Enregistrer le statut", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Supprimer définitivement", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Exporter", await client.GetStringAsync($"/admin/formulaires/{form.Id}/reponses"), StringComparison.Ordinal);

        var status = await client.PostAsync($"/admin/reponses/{submissionId}?handler=Status", Post(page, ("status", "entered")));
        var export = await client.GetAsync($"/admin/formulaires/{form.Id}/export");

        Assert.NotEqual(HttpStatusCode.OK, export.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, (await client.GetAsync($"/admin/reponses/{submissionId}/suppression")).StatusCode);
        Assert.NotEqual($"/admin/reponses/{submissionId}?statut=modifie", status.Headers.Location?.OriginalString);

        await using var db = factory.Database.CreateContext();
        Assert.Equal("new", (await db.Submissions.SingleAsync(s => s.Id == submissionId)).Status);
        Assert.False(await db.AuditLog.AnyAsync(a => a.Action == AuditActions.SubmissionsExported && a.Target == $"form:{form.Id}"));
    }

    [Fact]
    public async Task Editor_exports_filtered_submissions_as_csv()
    {
        var (form, _) = await SeedSubmissionAsync("bo-export");
        var client = await LoggedInAsync(AppRoles.Editor, "bo-export");

        var list = await client.GetStringAsync($"/admin/formulaires/{form.Id}/reponses?site=Site+fictif");
        Assert.Contains($"/admin/formulaires/{form.Id}/export?site=Site%20fictif", list, StringComparison.Ordinal);

        var response = await client.GetAsync($"/admin/formulaires/{form.Id}/export?site=Site+fictif");
        var content = await response.Content.ReadAsByteArrayAsync();
        var csv = Encoding.UTF8.GetString(content[3..]);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
        Assert.StartsWith("bo-export-reponses-", response.Content.Headers.ContentDisposition!.FileNameStar ?? response.Content.Headers.ContentDisposition.FileName, StringComparison.Ordinal);
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.StartsWith("Dossier;Reçue le;Statut;Version;Nom;Lieu de travail\r\n", csv, StringComparison.Ordinal);
        Assert.Contains("bo-export-0001;", csv, StringComparison.Ordinal);
        Assert.Contains(";Nouvelle;1;DUPONT;Site fictif", csv, StringComparison.Ordinal);

        var empty = Encoding.UTF8.GetString((await client.GetByteArrayAsync($"/admin/formulaires/{form.Id}/export?site=Autre"))[3..]);
        Assert.DoesNotContain("bo-export-0001", empty, StringComparison.Ordinal);

        Assert.Equal(
            ["1", "0"],
            (await AuditAsyncWithDetail($"form:{form.Id}", AuditActions.SubmissionsExported)));
    }

    [Fact]
    public async Task Admin_deletes_a_submission_after_typing_its_case_number()
    {
        var (form, submissionId) = await SeedSubmissionAsync("bo-suppr");
        var client = await LoggedInAsync(AppRoles.Admin);

        Assert.Contains("Supprimer définitivement", await client.GetStringAsync($"/admin/reponses/{submissionId}"), StringComparison.Ordinal);
        var confirm = await client.GetStringAsync($"/admin/reponses/{submissionId}/suppression");

        var wrong = await client.PostAsync($"/admin/reponses/{submissionId}/suppression", Post(confirm, ("confirmation", "bo-suppr-0002")));
        Assert.Equal(HttpStatusCode.OK, wrong.StatusCode);
        Assert.Contains("rien n'a été supprimé", await wrong.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var done = await client.PostAsync($"/admin/reponses/{submissionId}/suppression", Post(confirm, ("confirmation", "BO-SUPPR-0001")));
        Assert.Equal(HttpStatusCode.Redirect, done.StatusCode);
        Assert.Equal($"/admin/formulaires/{form.Id}/reponses?supprime=1", done.Headers.Location!.OriginalString);
        Assert.Contains("supprimée définitivement", await client.GetStringAsync(done.Headers.Location.OriginalString), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/admin/reponses/{submissionId}")).StatusCode);

        await using var db = factory.Database.CreateContext();
        Assert.False(await db.Submissions.AnyAsync(s => s.Id == submissionId));
        Assert.Equal(["bo-suppr-0001"], await AuditAsyncWithDetail($"submission:{submissionId}", AuditActions.SubmissionDeleted));
    }

    [Fact]
    public async Task Editor_cannot_delete()
    {
        var (_, submissionId) = await SeedSubmissionAsync("bo-suppr-editeur");
        var client = await LoggedInAsync(AppRoles.Editor, "bo-suppr-editeur");

        var page = await client.GetStringAsync($"/admin/reponses/{submissionId}");
        Assert.DoesNotContain("Supprimer définitivement", page, StringComparison.Ordinal);

        var response = await client.PostAsync($"/admin/reponses/{submissionId}/suppression", Post(page, ("confirmation", "bo-suppr-editeur-0001")));

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("supprime=1", response.Headers.Location?.OriginalString ?? string.Empty, StringComparison.Ordinal);
        await using var db = factory.Database.CreateContext();
        Assert.True(await db.Submissions.AnyAsync(s => s.Id == submissionId));
    }

    private async Task<List<string?>> AuditAsyncWithDetail(string target, string action)
    {
        await using var db = factory.Database.CreateContext();
        return await db.AuditLog.Where(a => a.Target == target && a.Action == action).OrderBy(a => a.OccurredAt).Select(a => a.Detail).ToListAsync();
    }
}
