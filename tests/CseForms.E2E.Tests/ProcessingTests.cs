using System.Text;
using System.Text.RegularExpressions;
using CseForms.E2E.Tests.Infrastructure;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace CseForms.E2E.Tests;

/// <summary>Traitement d'une inscription : statut, historique, export CSV, suppression définitive.</summary>
public sealed partial class ProcessingTests(E2EEnvironment app) : E2ETestBase(app)
{
    [E2EFact]
    public Task Admin_processes_exports_then_deletes_a_submission() => RunAsync(Desktop, async page =>
    {
        var caseNumber = await FictiveSubmissions.SubmitCdiAsync(App, nom: "Traitement");
        await LoginAsync(page, App.Admin);
        var detailUrl = await BackOfficeTests.OpenSubmissionAsync(page, caseNumber);

        // Conservation de l'inscription : 180 jours à partir du traitement.
        await Expect(page.GetByText("180 jours à partir du traitement")).ToBeVisibleAsync();
        await page.GetByLabel("Statut", new() { Exact = true }).SelectOptionAsync(new SelectOptionValue { Label = "Saisie dans le logiciel CSE" });
        await page.GetByRole(AriaRole.Button, new() { Name = "Enregistrer le statut" }).ClickAsync();

        await Expect(page.GetByText("Statut enregistré : Saisie dans le logiciel CSE.")).ToBeVisibleAsync();
        await Expect(page.GetByText("Suppression prévue le")).ToBeVisibleAsync();
        var history = page.Locator("ul.history");
        await Expect(history).ToContainTextAsync("Statut changé : « Nouvelle » vers « Saisie dans le logiciel CSE » par Admin Fictif");
        await Expect(history).ToContainTextAsync("Réponse reçue");

        // Export CSV limité au dossier par le filtre de la liste.
        await page.GetByRole(AriaRole.Link, new() { Name = "Fiche d'inscription au CSE" }).ClickAsync();
        await page.Locator("#q").FillAsync(caseNumber);
        await page.GetByRole(AriaRole.Button, new() { Name = "Filtrer" }).ClickAsync();
        var download = await page.RunAndWaitForDownloadAsync(() =>
            page.GetByRole(AriaRole.Link, new() { NameRegex = ExportLink() }).ClickAsync());
        Assert.EndsWith(".csv", download.SuggestedFilename, StringComparison.Ordinal);

        await using var stream = await download.CreateReadStreamAsync();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var lines = (await reader.ReadToEndAsync()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("Dossier;Reçue le;Statut;Version;Civilité;Nom;Prénom", lines[0], StringComparison.Ordinal);
        Assert.StartsWith($"{caseNumber};", lines[1], StringComparison.Ordinal);
        Assert.Contains(";Saisie dans le logiciel CSE;", lines[1], StringComparison.Ordinal);
        Assert.Contains(";TRAITEMENT;", lines[1], StringComparison.Ordinal);

        // Suppression : le numéro de dossier est demandé pour confirmer.
        await page.GotoAsync(detailUrl);
        await page.GetByRole(AriaRole.Link, new() { Name = "Supprimer définitivement cette réponse" }).ClickAsync();
        await page.GetByLabel(new Regex("Pour confirmer")).FillAsync("INS-0000-0000");
        await page.GetByRole(AriaRole.Button, new() { Name = "Supprimer définitivement" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync("rien n'a été supprimé");

        await page.GetByLabel(new Regex("Pour confirmer")).FillAsync(caseNumber);
        await page.GetByRole(AriaRole.Button, new() { Name = "Supprimer définitivement" }).ClickAsync();
        await Expect(page.GetByText("La réponse a été supprimée définitivement.")).ToBeVisibleAsync();

        var gone = await page.GotoAsync(detailUrl);
        Assert.Equal(404, gone!.Status);
    });

    [GeneratedRegex("^Exporter ces 1 réponse")]
    private static partial Regex ExportLink();
}
