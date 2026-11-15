using CseForms.E2E.Tests.Infrastructure;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace CseForms.E2E.Tests;

/// <summary>Sauts entre sections et pages de fin sur le formulaire public (<c>Forms/parcours.json</c>).</summary>
public sealed class JumpTests(E2EEnvironment app) : E2ETestBase(app)
{
    private const string FormUrl = $"/f/{E2EEnvironment.JumpFormSlug}";

    [E2EFact]
    public Task Answer_leading_to_an_ending_submits_at_once_and_shows_that_ending() => RunAsync(Mobile, async page =>
    {
        var browserErrors = WatchBrowserErrors(page);
        await page.GotoAsync(FormUrl);
        await Expect(page.GetByText("Étape 1 sur 4")).ToBeVisibleAsync();

        await page.Locator("#f-salarie").GetByText("Non").ClickAsync();
        await Expect(page.GetByText("Étape 1 sur 1")).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Suivant" })).ToBeHiddenAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Envoyer" }).ClickAsync();

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Inscription réservée aux salariés" })).ToBeVisibleAsync();
        await Expect(page.GetByText("Cette sortie fictive est réservée aux salariés de l'entreprise.")).ToBeVisibleAsync();
        await Expect(page.Locator(".case-number")).ToHaveTextAsync(new System.Text.RegularExpressions.Regex("^PAR-[0-9]{4}-[0-9]{4}$"));
        await Expect(page.GetByText("Texte de confirmation fictif.")).ToHaveCountAsync(0);
        Assert.Empty(browserErrors);
    });

    [E2EFact]
    public Task Jump_skips_sections_and_the_path_follows_the_answers() => RunAsync(Mobile, async page =>
    {
        var browserErrors = WatchBrowserErrors(page);
        await page.GotoAsync(FormUrl);

        await page.Locator("#f-salarie").GetByText("Oui").ClickAsync();
        await Next(page, "Étape 2 sur 4");

        // Soirée : la section « Journée » est sautée.
        await page.Locator("#f-nom").FillAsync("Exemple");
        await page.Locator("#f-formule").GetByText("Soirée").ClickAsync();
        await Next(page, "Étape 3 sur 4");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Soirée" })).ToBeVisibleAsync();

        // Venir accompagné termine le formulaire sur la liste d'attente.
        await page.Locator("#f-accompagnant").GetByText("Oui").ClickAsync();
        await Expect(page.GetByText("Étape 3 sur 3")).ToBeVisibleAsync();
        await page.Locator("#f-accompagnant").GetByText("Non").ClickAsync();
        await Next(page, "Étape 4 sur 4");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Pour finir" })).ToBeVisibleAsync();

        // Retour en arrière et changement d'avis : le parcours se recalcule.
        await page.GetByRole(AriaRole.Button, new() { Name = "Précédent" }).ClickAsync();
        await page.Locator("#f-accompagnant").GetByText("Oui").ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Envoyer" }).ClickAsync();

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Vous êtes sur liste d'attente" })).ToBeVisibleAsync();
        Assert.Empty(browserErrors);
    });

    private static async Task Next(IPage page, string expectedStep)
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Suivant" }).ClickAsync();
        await Expect(page.GetByText(expectedStep)).ToBeVisibleAsync();
    }
}
