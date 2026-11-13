using System.Net;
using CseForms.Application.Settings;
using CseForms.Domain.Settings;
using Microsoft.EntityFrameworkCore;

namespace CseForms.Api.Tests;

/// <summary>Notifications d'un formulaire dans le back-office : enregistrement, aperçu, désactivation, droits.</summary>
public partial class BackOfficeTests
{
    private static async Task<HttpResponseMessage> PostNotificationsAsync(HttpClient client, Guid formId, string handler, Dictionary<string, string>? fields = null)
    {
        var url = $"/admin/formulaires/{formId}/notifications";
        var values = new Dictionary<string, string>(fields ?? [])
        {
            ["__RequestVerificationToken"] = Token(await client.GetStringAsync(url)),
        };
        return await client.PostAsync($"{url}?handler={handler}", new FormUrlEncodedContent(values));
    }

    private async Task EnsurePublicBaseUrlAsync()
    {
        await using var db = factory.Database.CreateContext();
        if (await db.Settings.FindAsync(SettingKeys.PublicBaseUrl) is null)
        {
            db.Settings.Add(new Setting(SettingKeys.PublicBaseUrl, "https://formulaires.example.test"));
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Editor_sets_previews_and_removes_the_notifications_of_a_form()
    {
        await EnsurePublicBaseUrlAsync();
        var form = await SeedLifecycleFormAsync("bo-notif");
        var client = await EditorOnAsync("bo-notif");
        Assert.Contains($"/admin/formulaires/{form.Id}/notifications", await client.GetStringAsync($"/admin/formulaires/{form.Id}/reglages"));
        Assert.Contains("Aucune notification", await client.GetStringAsync($"/admin/formulaires/{form.Id}/notifications"));

        var saved = await PostNotificationsAsync(client, form.Id, "Save", new()
        {
            ["recipients"] = "Bureau@Example.test\r\nelu@example.test",
            ["subject"] = "Dossier {numero}",
            ["body"] = "",
        });
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);

        var page = await client.GetStringAsync(saved.Headers.Location!.OriginalString);
        Assert.Contains("Notifications enregistrées", page);
        Assert.Contains("bureau@example.test&#xA;elu@example.test</textarea>", page);
        Assert.Contains("Dossier TST-", page);
        Assert.Contains("https://formulaires.example.test/admin/reponses/", page);

        await using (var db = factory.Database.CreateContext())
        {
            var rule = await db.NotificationRules.AsNoTracking().SingleAsync(r => r.FormId == form.Id);
            Assert.Equal(["bureau@example.test", "elu@example.test"], rule.Recipients);
            Assert.Equal("Dossier {numero}", rule.Subject);
            Assert.Null(rule.Body);
        }

        Assert.Equal(HttpStatusCode.Redirect, (await PostNotificationsAsync(client, form.Id, "Remove")).StatusCode);
        await using (var db = factory.Database.CreateContext())
        {
            Assert.False(await db.NotificationRules.AnyAsync(r => r.FormId == form.Id));
            Assert.False(await db.AuditLog.AnyAsync(a => a.Detail != null && a.Detail.Contains("example.test")));
        }

        var actions = (await AuditAsync($"form:{form.Id}")).Select(e => e.Action).ToList();
        Assert.Equal(["form.notifications", "form.notifications.remove"], actions);
    }

    [Fact]
    public async Task Invalid_notifications_are_refused_with_a_message_and_the_input_kept()
    {
        var form = await SeedLifecycleFormAsync("bo-notif-faux");
        var client = await EditorOnAsync("bo-notif-faux");

        var response = await PostNotificationsAsync(client, form.Id, "Save", new()
        {
            ["recipients"] = "bureau@example.test",
            ["subject"] = "Inscription de {nom}",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadAsStringAsync();
        Assert.Contains("Variable(s) inconnue(s) : {nom}", page);
        Assert.Contains("value=\"Inscription de {nom}\"", page);
        await using var db = factory.Database.CreateContext();
        Assert.False(await db.NotificationRules.AnyAsync(r => r.FormId == form.Id));
    }

    [Fact]
    public async Task Readers_outsiders_and_forged_posts_cannot_change_notifications()
    {
        var form = await SeedLifecycleFormAsync("bo-notif-droits");

        var reader = await CreateUserAsync("reader");
        Assert.Equal(0, await RunCliAsync(string.Empty, "grant", "--email", reader, "--form", "bo-notif-droits"));
        var readerClient = Client();
        await LoginAsync(readerClient, reader, Password);
        Assert.Equal(BackOfficeAccessDenied, (await readerClient.GetAsync($"/admin/formulaires/{form.Id}/notifications")).Headers.Location?.AbsolutePath);

        var outsiderClient = await LoggedInAsync("editor");
        Assert.Equal(HttpStatusCode.NotFound, (await outsiderClient.GetAsync($"/admin/formulaires/{form.Id}/notifications")).StatusCode);

        var editorClient = await EditorOnAsync("bo-notif-droits");
        var forged = await editorClient.PostAsync(
            $"/admin/formulaires/{form.Id}/notifications?handler=Save",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["recipients"] = "pirate@example.test" }));
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);

        await using var db = factory.Database.CreateContext();
        Assert.False(await db.NotificationRules.AnyAsync(r => r.FormId == form.Id));
        Assert.Contains(await AuditAsync($"form:{form.Id}"), e => e.Action == "access.denied");
    }
}
