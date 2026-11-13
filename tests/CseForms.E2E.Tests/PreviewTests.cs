using System.Text;
using CseForms.E2E.Tests.Infrastructure;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace CseForms.E2E.Tests;

/// <summary>Aperçu d'un brouillon avant publication : cadre mobile et ordinateur, saisie contrôlée, aucun envoi.</summary>
public sealed class PreviewTests(E2EEnvironment app) : E2ETestBase(app)
{
    private const string Slug = "e2e-apercu";

    private static readonly string DraftFile =
        $$"""
        {"slug":"{{Slug}}","title":"Sortie fictive","access":{"mode":"public"},"retentionDays":30,
         "ui":{"confirmationText":"Texte de confirmation fictif."},
         "sections":[{"id":"s","title":"Participant","fields":[{"id":"nom","type":"text","label":"Nom du participant","required":true}]}]}
        """;

    [E2EFact]
    public Task Draft_is_previewed_in_mobile_and_desktop_widths_without_sending_anything() => RunAsync(Desktop, async page =>
    {
        var errors = WatchBrowserErrors(page);
        var posts = new List<string>();
        page.Request += (_, request) =>
        {
            if (request.Method == "POST" && request.Url.Contains("/api/", StringComparison.Ordinal))
            {
                posts.Add(request.Url);
            }
        };

        await LoginAsync(page, App.Admin);
        await page.GotoAsync("/admin/formulaires/import");
        await page.Locator("#definition").SetInputFilesAsync(new FilePayload
        {
            Name = "sortie.json",
            MimeType = "application/json",
            Buffer = Encoding.UTF8.GetBytes(DraftFile),
        });
        await page.GetByRole(AriaRole.Button, new() { Name = "Enregistrer comme brouillon" }).ClickAsync();

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Aperçu : Sortie fictive" })).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync("rien n'a été publié");

        // Largeur mobile : le cadre fait 390 px, les règles CSS du smartphone s'appliquent.
        var frameElement = page.Locator("iframe.preview-frame");
        var frame = page.FrameLocator("iframe.preview-frame");
        await Expect(frame.GetByRole(AriaRole.Heading, new() { Name = "Sortie fictive", Level = 1 })).ToBeVisibleAsync();
        await Expect(frame.GetByRole(AriaRole.Note)).ToContainTextAsync("l'envoi est désactivé");
        var mobileWidth = (await frameElement.BoundingBoxAsync())!.Width;

        // Contrôles du formulaire public : champ obligatoire vide refusé.
        await frame.GetByRole(AriaRole.Button, new() { Name = "Envoyer" }).ClickAsync();
        await Expect(frame.GetByText("« Nom du participant » est obligatoire.").First).ToBeVisibleAsync();

        // Saisie valide : résultat simulé, rien n'est envoyé.
        await frame.GetByLabel("Nom du participant").FillAsync("Fictif");
        await frame.GetByRole(AriaRole.Button, new() { Name = "Envoyer" }).ClickAsync();
        await Expect(frame.GetByRole(AriaRole.Heading, new() { Name = "Aperçu : la saisie est valide" })).ToBeVisibleAsync();
        await Expect(frame.GetByText("Texte de confirmation fictif.")).ToBeVisibleAsync();

        await page.GetByRole(AriaRole.Link, new() { Name = "Ordinateur" }).ClickAsync();
        await Expect(page.Locator("iframe.preview-frame.desktop")).ToBeVisibleAsync();
        await Expect(frame.GetByRole(AriaRole.Heading, new() { Name = "Sortie fictive", Level = 1 })).ToBeVisibleAsync();
        var desktopWidth = (await frameElement.BoundingBoxAsync())!.Width;

        Assert.InRange(mobileWidth, 390, 420);
        Assert.True(desktopWidth > 900, $"Largeur ordinateur : {desktopWidth}");
        Assert.Empty(posts);
        Assert.Empty(errors);

        // Toujours hors ligne avant la publication, en ligne après.
        var offline = await page.APIRequest.GetAsync($"/api/public/forms/{Slug}");
        Assert.Equal(404, offline.Status);
        await page.GetByRole(AriaRole.Button, new() { Name = "Publier ce brouillon" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync("Brouillon publié");
        var online = await page.APIRequest.GetAsync($"/api/public/forms/{Slug}");
        Assert.Equal(200, online.Status);
    });
}
