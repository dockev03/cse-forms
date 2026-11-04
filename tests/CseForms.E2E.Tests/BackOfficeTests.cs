using System.Text.RegularExpressions;
using CseForms.E2E.Tests.Infrastructure;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace CseForms.E2E.Tests;

/// <summary>Back-office : connexion, droits par formulaire, liste et fiche des réponses.</summary>
public sealed partial class BackOfficeTests(E2EEnvironment app) : E2ETestBase(app)
{
    private const string InvalidCredentials = "Identifiant ou mot de passe incorrect.";

    [E2EFact]
    public Task Failed_login_shows_the_same_message_whether_the_account_exists_or_not() => RunAsync(Desktop, async page =>
    {
        await page.GotoAsync("/admin/connexion");
        await SubmitLoginAsync(page, App.Reader.Email, "mauvais-mot-de-passe");
        await Expect(page.GetByRole(AriaRole.Alert)).ToHaveTextAsync(InvalidCredentials);

        await SubmitLoginAsync(page, "inconnu.e2e@example.test", "mauvais-mot-de-passe");
        await Expect(page.GetByRole(AriaRole.Alert)).ToHaveTextAsync(InvalidCredentials);
        await Expect(page).ToHaveURLAsync(LoginUrl());
    });

    [E2EFact]
    public Task Deep_link_goes_through_login_then_back_to_the_requested_page() => RunAsync(Desktop, async page =>
    {
        var caseNumber = await FictiveSubmissions.SubmitCdiAsync(App);
        await LoginAsync(page, App.Admin);
        var detailUrl = await OpenSubmissionAsync(page, caseNumber);
        await Logout(page);

        await page.GotoAsync(detailUrl);
        await Expect(page).ToHaveURLAsync(LoginUrl());
        await SubmitLoginAsync(page, App.Admin.Email, App.Admin.Password);

        await Expect(page).ToHaveURLAsync(detailUrl);
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = $"Dossier {caseNumber}" })).ToBeVisibleAsync();
    });

    [E2EFact]
    public Task Logout_closes_the_session() => RunAsync(Desktop, async page =>
    {
        await LoginAsync(page, App.Reader);
        await Logout(page);

        await page.GotoAsync("/admin");
        await Expect(page).ToHaveURLAsync(LoginUrl());
        await page.GoBackAsync();
        await page.ReloadAsync();
        await Expect(page).ToHaveURLAsync(LoginUrl());
    });

    [E2EFact]
    public Task Account_without_rights_on_the_form_gets_not_found() => RunAsync(Desktop, async page =>
    {
        var caseNumber = await FictiveSubmissions.SubmitCdiAsync(App);
        await LoginAsync(page, App.Admin);
        var detailUrl = await OpenSubmissionAsync(page, caseNumber);
        var listUrl = await page.GetByRole(AriaRole.Link, new() { Name = "Fiche d'inscription au CSE" }).GetAttributeAsync("href");
        await Logout(page);

        await LoginAsync(page, App.Outsider);
        await Expect(page.GetByText("Aucun formulaire ne vous est attribué")).ToBeVisibleAsync();

        foreach (var url in new[] { detailUrl, listUrl! })
        {
            var response = await page.GotoAsync(url);
            Assert.Equal(404, response!.Status);
            await Expect(page.Locator("body")).ToHaveTextAsync("Page introuvable.");
        }
    });

    [E2EFact]
    public Task Granted_reader_finds_a_submission_and_reads_it_with_the_form_labels() => RunAsync(Desktop, async page =>
    {
        var caseNumber = await FictiveSubmissions.SubmitCdiAsync(App, nom: "Lecture");
        await LoginAsync(page, App.Reader);
        await OpenSubmissionAsync(page, caseNumber);

        await Expect(Value(page, "Nom")).ToHaveTextAsync("LECTURE");
        await Expect(Value(page, "Prénom")).ToHaveTextAsync("Camille");
        await Expect(Value(page, "Date d'embauche")).ToHaveTextAsync("04/03/2024");
        await Expect(Value(page, "Lieu de travail")).ToHaveTextAsync("Abrest");

        // CDI : la date de fin, masquée par la condition, n'a été ni validée ni enregistrée.
        await Expect(page.GetByText("Date de fin de contrat")).ToHaveCountAsync(0);
        // Champ de confirmation non conservé.
        await Expect(page.GetByText("Confirmation de l'email personnel")).ToHaveCountAsync(0);

        var children = page.Locator("table").Filter(new() { HasText = "Date de naissance" });
        await Expect(children.Locator("tbody tr")).ToHaveCountAsync(1);
        await Expect(children.Locator("tbody tr").First).ToContainTextAsync("15/06/2020");

        // Lecteur : consultation seule, pas de régénération du PDF.
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Régénérer le PDF" })).ToHaveCountAsync(0);
    });

    /// <summary>Retrouve un dossier par son numéro dans la liste du formulaire, ouvre sa fiche et renvoie son adresse.</summary>
    internal static async Task<string> OpenSubmissionAsync(IPage page, string caseNumber)
    {
        await page.GotoAsync("/admin");
        await page.GetByRole(AriaRole.Link, new() { Name = "Fiche d'inscription au CSE" }).ClickAsync();
        await page.Locator("#q").FillAsync(caseNumber);
        await page.GetByRole(AriaRole.Button, new() { Name = "Filtrer" }).ClickAsync();

        await Expect(page.GetByRole(AriaRole.Status)).ToHaveTextAsync("1 réponse(s)");
        await page.GetByRole(AriaRole.Link, new() { Name = caseNumber, Exact = true }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = $"Dossier {caseNumber}" })).ToBeVisibleAsync();
        return new Uri(page.Url).PathAndQuery;
    }

    private static async Task Logout(IPage page)
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Se déconnecter" }).ClickAsync();
        await Expect(page).ToHaveURLAsync(LoginUrl());
    }

    private static ILocator Value(IPage page, string label) =>
        page.Locator("dl.values > div").Filter(new() { Has = page.Locator("dt", new() { HasTextRegex = new Regex($"^{Regex.Escape(label)}$") }) }).Locator("dd");

    [GeneratedRegex("/admin/connexion")]
    private static partial Regex LoginUrl();
}
