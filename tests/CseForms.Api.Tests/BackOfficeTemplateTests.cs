using System.Net;
using CseForms.Domain.Forms;
using Microsoft.EntityFrameworkCore;

namespace CseForms.Api.Tests;

/// <summary>Modèles de formulaires depuis le back-office : catalogue, création, enregistrement, suppression, droits.</summary>
public partial class BackOfficeTests
{
    private const string TemplatesUrl = "/admin/formulaires/modeles";

    private static async Task<HttpResponseMessage> PostWithTokenAsync(HttpClient client, string url, Dictionary<string, string> values)
    {
        values["__RequestVerificationToken"] = Token(await client.GetStringAsync(url));
        return await client.PostAsync(url, new FormUrlEncodedContent(values));
    }

    [Fact]
    public async Task Admin_creates_a_form_from_a_built_in_template_and_publishes_it_from_the_preview()
    {
        var admin = await LoggedInAsync("admin");
        Assert.Contains(TemplatesUrl, await admin.GetStringAsync("/admin"));
        var catalog = await admin.GetStringAsync(TemplatesUrl);
        Assert.Contains("Questionnaire de satisfaction", catalog);
        Assert.Contains("Fourni avec l", catalog);

        var page = await admin.GetStringAsync($"{TemplatesUrl}/satisfaction");
        Assert.Contains("Accusé de réception par email : non", page);

        var response = await PostWithTokenAsync(admin, $"{TemplatesUrl}/satisfaction", new() { ["slug"] = "bo-avis-sortie", ["title"] = "Avis sur la sortie fictive" });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.OriginalString;
        Assert.Contains("fait=modele", location);
        var formId = Guid.Parse(location.Split('/')[3]);

        Assert.Equal(HttpStatusCode.NotFound, (await Client().GetAsync("/api/public/forms/bo-avis-sortie")).StatusCode);
        Assert.Contains("créé à partir du modèle", await admin.GetStringAsync($"/admin/formulaires/{formId}/apercu?fait=modele"));

        var published = await PostPreviewAsync(admin, formId, "Publish");
        Assert.Equal(HttpStatusCode.Redirect, published.StatusCode);
        Assert.True(await AcceptingAsync("bo-avis-sortie"));
        await using (var db = factory.Database.CreateContext())
        {
            var form = await db.Forms.AsNoTracking().SingleAsync(f => f.Id == formId);
            Assert.Equal(("SAT", 90, (string?)null), (form.CaseNumberPrefix, form.RetentionDays!.Value, form.AccessCodeHash));
        }

        Assert.Equal(["form.template.use", "form.import"], (await AuditAsync($"form:{formId}")).Select(e => e.Action).ToList());
    }

    [Fact]
    public async Task Admin_saves_a_form_as_template_uses_it_and_deletes_it()
    {
        var source = await SeedLifecycleFormAsync("bo-modele-source");
        var admin = await LoggedInAsync("admin");
        var saveUrl = $"/admin/formulaires/{source.Id}/modele";
        Assert.Contains(saveUrl, await admin.GetStringAsync($"/admin/formulaires/{source.Id}/reglages"));

        var saved = await PostWithTokenAsync(admin, saveUrl, new() { ["name"] = "Modèle fictif de sortie", ["description"] = "Description fictive." });
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        Assert.Contains("fait=enregistre", saved.Headers.Location!.OriginalString);

        Guid templateId;
        await using (var db = factory.Database.CreateContext())
        {
            templateId = (await db.FormTemplates.AsNoTracking().SingleAsync(t => t.Name == "Modèle fictif de sortie")).Id;
        }

        Assert.Contains("Modèle fictif de sortie", await admin.GetStringAsync(TemplatesUrl));

        // Même nom : refusé, saisie conservée.
        var taken = await PostWithTokenAsync(admin, saveUrl, new() { ["name"] = "modèle FICTIF de sortie" });
        Assert.Equal(HttpStatusCode.OK, taken.StatusCode);
        Assert.Contains("Un modèle porte déjà ce nom", await taken.Content.ReadAsStringAsync());

        var created = await PostWithTokenAsync(admin, $"{TemplatesUrl}/{templateId}", new() { ["slug"] = "bo-modele-copie", ["title"] = "Copie fictive" });
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var formId = Guid.Parse(created.Headers.Location!.OriginalString.Split('/')[3]);

        var deleted = await PostWithTokenAsync(admin, $"{TemplatesUrl}/{templateId}/suppression", []);
        Assert.Equal(HttpStatusCode.Redirect, deleted.StatusCode);
        Assert.Contains("fait=supprime", deleted.Headers.Location!.OriginalString);

        await using (var db = factory.Database.CreateContext())
        {
            Assert.False(await db.FormTemplates.AnyAsync(t => t.Id == templateId));
            Assert.True(await db.FormDrafts.AnyAsync(d => d.FormId == formId));
            Assert.Equal(FormStatus.Draft, (await db.Forms.AsNoTracking().SingleAsync(f => f.Id == formId)).Status);
        }

        Assert.Equal(["form.template.save", "form.template.delete"], (await AuditAsync($"template:{templateId}")).Select(e => e.Action).ToList());
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"{TemplatesUrl}/{templateId}/suppression")).StatusCode);
    }

    [Fact]
    public async Task Templates_are_reserved_to_admins_and_refusals_are_audited()
    {
        var source = await SeedLifecycleFormAsync("bo-modele-refus");
        var editor = await EditorOnAsync("bo-modele-refus");

        Assert.DoesNotContain(TemplatesUrl, await editor.GetStringAsync("/admin"));
        Assert.DoesNotContain($"/admin/formulaires/{source.Id}/modele", await editor.GetStringAsync($"/admin/formulaires/{source.Id}/reglages"));
        foreach (var url in new[] { TemplatesUrl, $"{TemplatesUrl}/satisfaction", $"/admin/formulaires/{source.Id}/modele" })
        {
            Assert.Equal(BackOfficeAccessDenied, (await editor.GetAsync(url)).Headers.Location?.AbsolutePath);
        }

        Assert.Contains(await AuditAsync($"form:{source.Id}"), e => e.Action == "access.denied");
        var admin = await LoggedInAsync("admin");
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"{TemplatesUrl}/inconnu")).StatusCode);
    }
}
