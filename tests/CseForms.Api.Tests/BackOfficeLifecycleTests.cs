using System.Net;
using System.Text.Json.Nodes;
using CseForms.Domain.Forms;
using CseForms.Application.Common;
using CseForms.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CseForms.Api.Tests;

/// <summary>Réglages d'un formulaire dans le back-office : fermeture, dates, plafond, code d'accès, droits.</summary>
public partial class BackOfficeTests
{
    private async Task<Form> SeedLifecycleFormAsync(string slug, string? accessCode = null)
    {
        await using var db = factory.Database.CreateContext();
        return await TestData.AddPublishedFormAsync(db, slug, configure: f =>
        {
            if (accessCode is not null)
            {
                f.SetAccessCodeHash(TestData.HashAccessCode(accessCode));
            }
        });
    }

    private async Task<Form> ReloadFormAsync(Guid formId)
    {
        await using var db = factory.Database.CreateContext();
        return await db.Forms.AsNoTracking().SingleAsync(f => f.Id == formId);
    }

    private async Task<bool> AcceptingAsync(string slug)
    {
        var body = await Client().GetStringAsync($"/api/public/forms/{slug}");
        return JsonNode.Parse(body)!["accepting"]!.GetValue<bool>();
    }

    private static async Task<HttpResponseMessage> PostSettingsAsync(HttpClient client, Guid formId, string handler, Dictionary<string, string>? fields = null)
    {
        var url = $"/admin/formulaires/{formId}/reglages";
        var values = new Dictionary<string, string>(fields ?? [])
        {
            ["__RequestVerificationToken"] = Token(await client.GetStringAsync(url)),
        };
        return await client.PostAsync($"{url}?handler={handler}", new FormUrlEncodedContent(values));
    }

    [Fact]
    public async Task Editor_closes_and_reopens_a_form_from_the_back_office()
    {
        var form = await SeedLifecycleFormAsync("bo-cycle");
        var client = await EditorOnAsync("bo-cycle");
        Assert.Contains($"/admin/formulaires/{form.Id}/reglages", await client.GetStringAsync("/admin"));
        Assert.True(await AcceptingAsync("bo-cycle"));

        var closed = await PostSettingsAsync(client, form.Id, "Close");
        Assert.Equal(HttpStatusCode.Redirect, closed.StatusCode);
        Assert.Contains("fait=ferme", closed.Headers.Location?.OriginalString);
        Assert.Equal(FormStatus.Closed, (await ReloadFormAsync(form.Id)).Status);
        Assert.False(await AcceptingAsync("bo-cycle"));
        Assert.Contains("Formulaire fermé", await client.GetStringAsync(closed.Headers.Location!.OriginalString));

        Assert.Equal(HttpStatusCode.Redirect, (await PostSettingsAsync(client, form.Id, "Reopen")).StatusCode);
        Assert.True(await AcceptingAsync("bo-cycle"));

        var actions = (await AuditAsync($"form:{form.Id}")).Select(e => e.Action).ToList();
        Assert.Equal(["form.close", "form.reopen"], actions);
    }

    [Fact]
    public async Task Schedule_and_maximum_are_saved_in_paris_time_and_applied_to_the_public_form()
    {
        var form = await SeedLifecycleFormAsync("bo-calendrier");
        var client = await EditorOnAsync("bo-calendrier");

        var saved = await PostSettingsAsync(client, form.Id, "Schedule", new()
        {
            ["opensAt"] = "2099-12-01T08:00",
            ["closesAt"] = "2099-12-20T18:30",
            ["maxResponses"] = "40",
        });

        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        var reloaded = await ReloadFormAsync(form.Id);
        Assert.Equal(new DateTimeOffset(2099, 12, 1, 7, 0, 0, TimeSpan.Zero), reloaded.OpensAt);
        Assert.Equal(new DateTimeOffset(2099, 12, 20, 17, 30, 0, TimeSpan.Zero), reloaded.ClosesAt);
        Assert.Equal(40, reloaded.MaxResponses);
        Assert.False(await AcceptingAsync("bo-calendrier"));

        var page = await client.GetStringAsync($"/admin/formulaires/{form.Id}/reglages");
        Assert.Contains("value=\"2099-12-01T08:00\"", page);
        Assert.Contains("pas encore ouvert", page);

        await PostSettingsAsync(client, form.Id, "Schedule", new() { ["opensAt"] = "", ["closesAt"] = "", ["maxResponses"] = "" });
        Assert.True(await AcceptingAsync("bo-calendrier"));
        Assert.Equal(2, (await AuditAsync($"form:{form.Id}")).Count(e => e.Action == "form.schedule"));
    }

    [Fact]
    public async Task Invalid_schedule_is_refused_with_a_message_and_the_input_kept()
    {
        var form = await SeedLifecycleFormAsync("bo-calendrier-faux");
        var client = await EditorOnAsync("bo-calendrier-faux");

        var reversed = await PostSettingsAsync(client, form.Id, "Schedule", new()
        {
            ["opensAt"] = "2099-12-20T08:00",
            ["closesAt"] = "2099-12-01T08:00",
        });
        Assert.Equal(HttpStatusCode.OK, reversed.StatusCode);
        var page = await reversed.Content.ReadAsStringAsync();
        Assert.Contains("postérieure", page);
        Assert.Contains("value=\"2099-12-20T08:00\"", page);

        var badCount = await PostSettingsAsync(client, form.Id, "Schedule", new() { ["maxResponses"] = "-3" });
        Assert.Contains("nombre entier positif", await badCount.Content.ReadAsStringAsync());

        var reloaded = await ReloadFormAsync(form.Id);
        Assert.Null(reloaded.OpensAt);
        Assert.Null(reloaded.MaxResponses);
        Assert.DoesNotContain(await AuditAsync($"form:{form.Id}"), e => e.Action == "form.schedule");
    }

    [Fact]
    public async Task Access_code_is_changed_twice_typed_and_never_echoed_nor_logged()
    {
        var form = await SeedLifecycleFormAsync("bo-code", accessCode: "1234");
        var client = await EditorOnAsync("bo-code");

        var mistyped = await PostSettingsAsync(client, form.Id, "AccessCode", new() { ["code"] = "975318", ["confirmation"] = "975381" });
        var mistypedPage = await mistyped.Content.ReadAsStringAsync();
        Assert.Contains("différentes", mistypedPage);
        Assert.DoesNotContain("975318", mistypedPage);

        var changed = await PostSettingsAsync(client, form.Id, "AccessCode", new() { ["code"] = "975318", ["confirmation"] = "975318" });
        Assert.Equal(HttpStatusCode.Redirect, changed.StatusCode);

        var hash = (await ReloadFormAsync(form.Id)).AccessCodeHash!;
        var hasher = factory.Services.GetRequiredService<IAccessCodeHasher>();
        Assert.True(hasher.Verify(hash, "975318"));
        Assert.False(hasher.Verify(hash, "1234"));

        Assert.Contains(await AuditAsync($"form:{form.Id}"), e => e.Action == "form.access_code");
        await using var db = factory.Database.CreateContext();
        Assert.False(await db.AuditLog.AnyAsync(a => (a.Detail != null && a.Detail.Contains("975318")) || a.Target!.Contains("975318")));
    }

    [Fact]
    public async Task Public_form_has_no_access_code_section()
    {
        var form = await SeedLifecycleFormAsync("bo-public");
        var client = await EditorOnAsync("bo-public");

        var page = await client.GetStringAsync($"/admin/formulaires/{form.Id}/reglages");

        Assert.DoesNotContain("Nouveau code d", page);
    }

    [Fact]
    public async Task Readers_outsiders_and_forged_posts_cannot_change_a_form()
    {
        var form = await SeedLifecycleFormAsync("bo-reglages-droits");

        var reader = await CreateUserAsync("reader");
        Assert.Equal(0, await RunCliAsync(string.Empty, "grant", "--email", reader, "--form", "bo-reglages-droits"));
        var readerClient = Client();
        await LoginAsync(readerClient, reader, Password);
        Assert.DoesNotContain("/reglages", await readerClient.GetStringAsync("/admin"));
        Assert.Equal(BackOfficeAccessDenied, (await readerClient.GetAsync($"/admin/formulaires/{form.Id}/reglages")).Headers.Location?.AbsolutePath);

        var outsider = await CreateUserAsync("editor");
        var outsiderClient = Client();
        await LoginAsync(outsiderClient, outsider, Password);
        Assert.Equal(HttpStatusCode.NotFound, (await outsiderClient.GetAsync($"/admin/formulaires/{form.Id}/reglages")).StatusCode);

        var editorClient = await EditorOnAsync("bo-reglages-droits");
        var forged = await editorClient.PostAsync($"/admin/formulaires/{form.Id}/reglages?handler=Close", new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);

        Assert.Equal(FormStatus.Published, (await ReloadFormAsync(form.Id)).Status);
        var audit = await AuditAsync($"form:{form.Id}");
        Assert.DoesNotContain(audit, e => e.Action == "form.close");
        Assert.Contains(audit, e => e.Action == "access.denied");
    }
}
