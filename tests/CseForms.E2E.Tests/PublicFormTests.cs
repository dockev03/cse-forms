using System.Text.RegularExpressions;
using CseForms.E2E.Tests.Infrastructure;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace CseForms.E2E.Tests;

/// <summary>Formulaire public d'inscription, rempli sur smartphone.</summary>
public sealed partial class PublicFormTests(E2EEnvironment app) : E2ETestBase(app)
{
    private const string FormUrl = $"/f/{E2EEnvironment.FormSlug}";

    [E2EFact]
    public Task Inscription_is_filled_step_by_step_and_returns_a_case_number() => RunAsync(Mobile, async page =>
    {
        var browserErrors = WatchBrowserErrors(page);
        await page.GotoAsync($"{FormUrl}?site=Riom");
        await Expect(page.GetByText("Étape 1 sur 6")).ToBeVisibleAsync();

        await page.Locator("#f-accessCode").FillAsync(App.AccessCode);

        // État civil
        await page.Locator("#f-civilite").GetByText("Mme").ClickAsync();
        await page.Locator("#f-nom").FillAsync("Exemple-Test");
        await page.Locator("#f-prenom").FillAsync("dominique");
        await Next(page, "Étape 2 sur 6");

        // Informations professionnelles : la date de fin n'existe que hors CDI.
        await Expect(page.Locator("#f-site")).ToHaveValueAsync("Riom");
        await page.Locator("#f-contrat").GetByText("CDD").ClickAsync();
        await Expect(page.Locator("#f-dateFin")).ToBeVisibleAsync();
        await page.Locator("#f-contrat").GetByText("CDI").ClickAsync();
        await Expect(page.Locator("#f-dateFin")).ToBeHiddenAsync();
        await page.Locator("#f-contrat").GetByText("CDD").ClickAsync();
        await page.Locator("#f-dateEmbauche").FillAsync("2025-09-01");
        await page.Locator("#f-dateFin").FillAsync("2026-08-31");
        await Next(page, "Étape 3 sur 6");

        // Coordonnées
        await page.Locator("#f-adresse-street").FillAsync("2 avenue de l'Exemple");
        await page.Locator("#f-adresse-postalCode").FillAsync("63200");
        await page.Locator("#f-adresse-city").FillAsync("Riom");
        await page.Locator("#f-emailPerso").FillAsync("dominique.exemple@example.test");
        await page.Locator("#f-emailConfirmation").FillAsync("dominique.exemple@example.test");
        await Next(page, "Étape 4 sur 6");

        // Conjoint : facultatif.
        await Next(page, "Étape 5 sur 6");

        // Enfants : deux lignes ajoutées, la première retirée ; les indices se recalent.
        var add = page.GetByRole(AriaRole.Button, new() { Name = "Ajouter un enfant" });
        await add.ClickAsync();
        await add.ClickAsync();
        await FillChild(page, 0, "Premier", "2019-04-02");
        await FillChild(page, 1, "Second", "2022-11-20");
        await page.Locator("#f-enfants-0").GetByRole(AriaRole.Button, new() { Name = "Retirer cette ligne" }).ClickAsync();
        await Expect(page.Locator(".repeat-row")).ToHaveCountAsync(1);
        await Expect(page.Locator("#f-enfants-0-prenom")).ToHaveValueAsync("Second");
        await Next(page, "Étape 6 sur 6");

        // Validation
        await page.Locator("#f-rgpdLu").CheckAsync();
        await page.Locator("#f-attestation").CheckAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Envoyer" }).ClickAsync();

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Merci, votre réponse est enregistrée" })).ToBeVisibleAsync();
        await Expect(page.Locator(".case-number")).ToHaveTextAsync(CaseNumber());
        Assert.Empty(browserErrors);
    });

    [E2EFact]
    public Task Missing_and_invalid_answers_block_the_step_with_a_message_on_the_field() => RunAsync(Mobile, async page =>
    {
        await page.GotoAsync(FormUrl);

        await page.GetByRole(AriaRole.Button, new() { Name = "Suivant" }).ClickAsync();
        var summary = page.GetByRole(AriaRole.Alert);
        await Expect(summary).ToContainTextAsync("Certaines réponses sont à corriger");
        await Expect(page.Locator("#f-nom")).ToHaveAttributeAsync("aria-invalid", "true");
        await Expect(page.Locator("#f-nom-error")).ToBeVisibleAsync();
        await Expect(page.GetByText("Étape 1 sur 6")).ToBeVisibleAsync();

        await page.Locator("#f-civilite").GetByText("M.").ClickAsync();
        await page.Locator("#f-nom").FillAsync("Exemple");
        await page.Locator("#f-prenom").FillAsync("alex");
        await Next(page, "Étape 2 sur 6");
        await page.Locator("#f-contrat").GetByText("CDI").ClickAsync();
        await page.Locator("#f-dateEmbauche").FillAsync("2024-01-08");
        await page.Locator("#f-site").SelectOptionAsync("Abrest");
        await Next(page, "Étape 3 sur 6");

        await page.Locator("#f-adresse-street").FillAsync("3 place de l'Exemple");
        await page.Locator("#f-adresse-postalCode").FillAsync("03200");
        await page.Locator("#f-adresse-city").FillAsync("Abrest");
        await page.Locator("#f-emailPerso").FillAsync("pas-un-email");
        await page.Locator("#f-emailConfirmation").FillAsync("pas-un-email");
        await page.GetByRole(AriaRole.Button, new() { Name = "Suivant" }).ClickAsync();
        await Expect(page.Locator("#f-emailPerso-error")).ToContainTextAsync("n'est pas une adresse email valide");
        await Expect(page.GetByText("Étape 3 sur 6")).ToBeVisibleAsync();

        // Email corrigé mais confirmation différente : toujours bloqué.
        await page.Locator("#f-emailPerso").FillAsync("alex.exemple@example.test");
        await page.Locator("#f-emailConfirmation").FillAsync("autre@example.test");
        await page.GetByRole(AriaRole.Button, new() { Name = "Suivant" }).ClickAsync();
        await Expect(page.Locator("#f-emailPerso")).Not.ToHaveAttributeAsync("aria-invalid", "true");
        await Expect(page.Locator("#f-emailConfirmation-error")).ToContainTextAsync("doit être identique");
        await Expect(page.GetByText("Étape 3 sur 6")).ToBeVisibleAsync();
    });

    [E2EFact]
    public Task Wrong_access_code_is_refused_with_a_generic_message() => RunAsync(Mobile, async page =>
    {
        await page.GotoAsync($"{FormUrl}?site=Abrest");
        await page.Locator("#f-accessCode").FillAsync("0000");
        await FillMinimalCdi(page);

        await page.GetByRole(AriaRole.Button, new() { Name = "Envoyer" }).ClickAsync();
        await Expect(page.Locator("#f-accessCode-error")).ToHaveTextAsync("Code d'accès incorrect.");
        await Expect(page.Locator("#f-accessCode")).ToHaveAttributeAsync("aria-invalid", "true");
        await Expect(page.Locator(".case-number")).ToHaveCountAsync(0);
    });

    [E2EFact]
    public Task Site_prefill_only_accepts_an_active_site() => RunAsync(Mobile, async page =>
    {
        await page.GotoAsync($"{FormUrl}?site=Inconnu");
        await page.Locator("#f-civilite").GetByText("M.").ClickAsync();
        await page.Locator("#f-nom").FillAsync("Exemple");
        await page.Locator("#f-prenom").FillAsync("alex");
        await Next(page, "Étape 2 sur 6");
        await Expect(page.Locator("#f-site")).ToHaveValueAsync(string.Empty);
    });

    [E2EFact]
    public Task Public_page_loads_without_csp_violation_nor_third_party_request() => RunAsync(Mobile, async page =>
    {
        var browserErrors = WatchBrowserErrors(page);
        var foreignRequests = new List<string>();
        page.Request += (_, request) =>
        {
            if (!request.Url.StartsWith(App.BaseUrl, StringComparison.Ordinal))
            {
                foreignRequests.Add(request.Url);
            }
        };

        var response = await page.GotoAsync(FormUrl);
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Fiche d'inscription au CSE" })).ToBeVisibleAsync();

        Assert.Contains("default-src", response!.Headers["content-security-policy"]);
        Assert.Equal("nosniff", response.Headers["x-content-type-options"]);
        Assert.Empty(browserErrors);
        Assert.Empty(foreignRequests);
    });

    private static async Task FillMinimalCdi(IPage page)
    {
        await page.Locator("#f-civilite").GetByText("M.").ClickAsync();
        await page.Locator("#f-nom").FillAsync("Exemple");
        await page.Locator("#f-prenom").FillAsync("alex");
        await Next(page, "Étape 2 sur 6");
        await page.Locator("#f-contrat").GetByText("CDI").ClickAsync();
        await page.Locator("#f-dateEmbauche").FillAsync("2024-01-08");
        await Next(page, "Étape 3 sur 6");
        await page.Locator("#f-adresse-street").FillAsync("3 place de l'Exemple");
        await page.Locator("#f-adresse-postalCode").FillAsync("03200");
        await page.Locator("#f-adresse-city").FillAsync("Abrest");
        await page.Locator("#f-emailPerso").FillAsync("alex.exemple@example.test");
        await page.Locator("#f-emailConfirmation").FillAsync("alex.exemple@example.test");
        await Next(page, "Étape 4 sur 6");
        await Next(page, "Étape 5 sur 6");
        await Next(page, "Étape 6 sur 6");
        await page.Locator("#f-rgpdLu").CheckAsync();
        await page.Locator("#f-attestation").CheckAsync();
    }

    private static async Task Next(IPage page, string expectedStep)
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Suivant" }).ClickAsync();
        await Expect(page.GetByText(expectedStep)).ToBeVisibleAsync();
    }

    private static async Task FillChild(IPage page, int index, string prenom, string naissance)
    {
        await page.Locator($"#f-enfants-{index}-nom").FillAsync("Exemple-Test");
        await page.Locator($"#f-enfants-{index}-prenom").FillAsync(prenom);
        await page.Locator($"#f-enfants-{index}-naissance").FillAsync(naissance);
    }

    [GeneratedRegex(@"^INS-\d{4}-\d{4}$")]
    private static partial Regex CaseNumber();
}
