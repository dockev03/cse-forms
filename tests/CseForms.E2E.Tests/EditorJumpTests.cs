using System.Text;
using CseForms.E2E.Tests.Infrastructure;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace CseForms.E2E.Tests;

/// <summary>Éditeur visuel : page de fin et saut créés sans écrire de JSON, puis vérifiés dans l'aperçu.</summary>
public sealed class EditorJumpTests(E2EEnvironment app) : E2ETestBase(app)
{
    private const string Slug = "e2e-editeur-sauts";

    private static readonly string DraftFile =
        $$"""
        {"slug":"{{Slug}}","title":"Sortie fictive avec sauts","access":{"mode":"public"},"retentionDays":30,
         "sections":[
           {"id":"eligibilite","title":"Éligibilité","fields":[{"id":"salarie","type":"choice","label":"Êtes-vous salarié ?","required":true,"options":["Oui","Non"]}]},
           {"id":"inscription","title":"Inscription","fields":[{"id":"nom","type":"text","label":"Nom du participant","required":true}]}]}
        """;

    [E2EFact]
    public Task Ending_and_jump_are_built_in_the_editor_and_followed_in_the_preview() => RunAsync(Desktop, async page =>
    {
        var errors = WatchBrowserErrors(page);
        await LoginAsync(page, App.Admin);
        await page.GotoAsync("/admin/formulaires/import");
        await page.Locator("#definition").SetInputFilesAsync(new FilePayload
        {
            Name = "sauts.json",
            MimeType = "application/json",
            Buffer = Encoding.UTF8.GetBytes(DraftFile),
        });
        await page.GetByRole(AriaRole.Button, new() { Name = "Enregistrer comme brouillon" }).ClickAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "Modifier dans l'éditeur" }).ClickAsync();

        var tree = page.GetByRole(AriaRole.Navigation, new() { Name = "Structure du formulaire" });
        var panel = page.GetByRole(AriaRole.Region, new() { Name = "Propriétés de l'élément sélectionné" });

        // Page de fin
        await tree.GetByRole(AriaRole.Button, new() { Name = "Ajouter une page de fin" }).ClickAsync();
        await panel.GetByLabel("Titre", new() { Exact = true }).FillAsync("Réservé aux salariés");
        await panel.GetByLabel("Texte (facultatif)").FillAsync("Texte de fin fictif.");
        await Expect(tree.GetByRole(AriaRole.Button, new() { Name = "Réservé aux salariés" })).ToBeVisibleAsync();

        // Saut : « Non » termine le formulaire sur cette page.
        await tree.GetByRole(AriaRole.Button, new() { Name = "Éligibilité", Exact = true }).ClickAsync();
        await panel.GetByRole(AriaRole.Button, new() { Name = "Ajouter un saut" }).ClickAsync();
        var jump = panel.GetByRole(AriaRole.Group, new() { Name = "Saut 1" });
        await jump.GetByLabel("Valeur").SelectOptionAsync(new SelectOptionValue { Label = "Non" });
        await jump.GetByLabel("Destination du saut 1").SelectOptionAsync(new SelectOptionValue { Label = "Terminer : page « Réservé aux salariés »" });
        await Expect(tree.GetByText("saut", new() { Exact = true })).ToBeVisibleAsync();

        await page.GetByRole(AriaRole.Button, new() { Name = "Enregistrer le brouillon et voir l'aperçu" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Aperçu : Sortie fictive avec sauts" })).ToBeVisibleAsync();

        // Dans l'aperçu, « Non » saute la section suivante et mène à la page de fin.
        var frame = page.FrameLocator("iframe.preview-frame");
        await frame.GetByText("Non", new() { Exact = true }).ClickAsync();
        await Expect(frame.GetByLabel("Nom du participant")).ToBeHiddenAsync();
        await frame.GetByRole(AriaRole.Button, new() { Name = "Envoyer" }).ClickAsync();
        await Expect(frame.GetByRole(AriaRole.Heading, new() { Name = "Aperçu : la saisie est valide" })).ToBeVisibleAsync();
        await Expect(frame.GetByText("Page de fin : « Réservé aux salariés »")).ToBeVisibleAsync();
        await Expect(frame.GetByText("Texte de fin fictif.")).ToBeVisibleAsync();
        Assert.Empty(errors);
    });
}
