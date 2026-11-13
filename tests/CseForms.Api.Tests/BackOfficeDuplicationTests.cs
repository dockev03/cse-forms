using System.Net;
using CseForms.Domain.Forms;
using CseForms.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace CseForms.Api.Tests;

/// <summary>Duplication d'un formulaire depuis le back-office : copie hors ligne, réglages recopiés, droits, publication.</summary>
public partial class BackOfficeTests
{
    private static string DuplicateUrl(Guid formId) => $"/admin/formulaires/{formId}/duplication";

    private static async Task<HttpResponseMessage> PostDuplicateAsync(HttpClient client, Guid formId, string slug, string title)
    {
        var values = new Dictionary<string, string>
        {
            ["slug"] = slug,
            ["title"] = title,
            ["__RequestVerificationToken"] = Token(await client.GetStringAsync(DuplicateUrl(formId))),
        };
        return await client.PostAsync(DuplicateUrl(formId), new FormUrlEncodedContent(values));
    }

    [Fact]
    public async Task Admin_duplicates_a_form_with_its_settings_and_publishes_the_copy_from_the_preview()
    {
        var source = await SeedLifecycleFormAsync("bo-noel-2025");
        await using (var db = factory.Database.CreateContext())
        {
            db.NotificationRules.Add(new NotificationRule(source.Id, ["elus@example.test"], null, null));
            var tracked = await db.Forms.SingleAsync(f => f.Id == source.Id);
            tracked.SetPdfWatermark(true);
            tracked.SetMaxResponses(80);
            await db.SaveChangesAsync();
        }

        var editor = await EditorOnAsync("bo-noel-2025");
        var admin = await LoggedInAsync("admin");
        Assert.Contains(DuplicateUrl(source.Id), await admin.GetStringAsync($"/admin/formulaires/{source.Id}/reglages"));
        Assert.Contains("value=\"bo-noel-2025-copie\"", await admin.GetStringAsync(DuplicateUrl(source.Id)));

        var response = await PostDuplicateAsync(admin, source.Id, "bo-noel-2026", "Arbre de Noël fictif 2026");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.OriginalString;
        Assert.Contains("fait=copie", location);
        var copyId = Guid.Parse(location.Split('/')[3]);

        // Copie hors ligne, réglages recopiés, sauf le plafond propre à chaque édition.
        Assert.Equal(HttpStatusCode.NotFound, (await Client().GetAsync("/api/public/forms/bo-noel-2026")).StatusCode);
        await using (var db = factory.Database.CreateContext())
        {
            var copy = await db.Forms.Include(f => f.Versions).AsNoTracking().SingleAsync(f => f.Id == copyId);
            Assert.Equal((FormStatus.Draft, 0, true, (int?)null), (copy.Status, copy.Versions.Count, copy.PdfWatermark, copy.MaxResponses));
            Assert.True(await db.FormDrafts.AnyAsync(d => d.FormId == copyId));
            Assert.Equal(["elus@example.test"], (await db.NotificationRules.AsNoTracking().SingleAsync(r => r.FormId == copyId)).Recipients);
            Assert.Equal(1, await db.UserFormAccess.CountAsync(a => a.FormId == copyId));
        }

        // L'éditeur attribué à la source l'est aussi à la copie : il peut la publier depuis l'aperçu.
        Assert.Contains("Formulaire dupliqué", await editor.GetStringAsync($"/admin/formulaires/{copyId}/apercu?fait=copie"));
        var published = await PostPreviewAsync(editor, copyId, "Publish");
        Assert.Equal(HttpStatusCode.Redirect, published.StatusCode);
        Assert.True(await AcceptingAsync("bo-noel-2026"));
        Assert.True(await AcceptingAsync("bo-noel-2025"));
        await using (var db = factory.Database.CreateContext())
        {
            // Préfixe de la source, appliqué à la publication : les deux éditions partagent le même compteur.
            Assert.Equal("TST", (await db.Forms.AsNoTracking().SingleAsync(f => f.Id == copyId)).CaseNumberPrefix);
        }

        var audit = await AuditAsync($"form:{copyId}");
        Assert.Equal(["form.duplicate", "form.import"], audit.Select(e => e.Action).ToList());
    }

    [Fact]
    public async Task Duplication_refuses_a_taken_address_and_is_reserved_to_admins()
    {
        var source = await SeedLifecycleFormAsync("bo-dup-source");
        await SeedLifecycleFormAsync("bo-dup-pris");
        var admin = await LoggedInAsync("admin");

        var taken = await PostDuplicateAsync(admin, source.Id, "bo-dup-pris", "Titre fictif");
        var html = await taken.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, taken.StatusCode);
        Assert.Contains("déjà utilisée", html);
        Assert.Contains("value=\"Titre fictif\"", html);

        var editor = await EditorOnAsync("bo-dup-source");
        Assert.DoesNotContain(DuplicateUrl(source.Id), await editor.GetStringAsync($"/admin/formulaires/{source.Id}/reglages"));
        Assert.Equal(BackOfficeAccessDenied, (await editor.GetAsync(DuplicateUrl(source.Id))).Headers.Location?.AbsolutePath);
        Assert.Contains(await AuditAsync($"form:{source.Id}"), e => e.Action == "access.denied");

        await using var db = factory.Database.CreateContext();
        Assert.Equal(2, await db.Forms.CountAsync(f => f.Slug.StartsWith("bo-dup-")));
    }
}
