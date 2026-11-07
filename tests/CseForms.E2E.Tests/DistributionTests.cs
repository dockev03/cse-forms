using CseForms.E2E.Tests.Infrastructure;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace CseForms.E2E.Tests;

/// <summary>Diffusion de bout en bout : QR code par site et flyer PDF produit par Gotenberg.</summary>
public sealed class DistributionTests(E2EEnvironment app) : E2ETestBase(app)
{
    [E2EFact]
    public Task Admin_downloads_a_site_qr_code_and_a_flyer_with_the_access_code() => RunAsync(Desktop, async page =>
    {
        await LoginAsync(page, App.Admin);
        await page.GetByRole(AriaRole.Link, new() { Name = "QR code et flyer" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToContainTextAsync("Diffusion");
        await Expect(page.GetByText($"{E2EEnvironment.PublicBaseUrl}/f/{E2EEnvironment.FormSlug}", new() { Exact = true })).ToBeVisibleAsync();

        await page.Locator("#qr-site").SelectOptionAsync("Riom");
        var qr = await page.RunAndWaitForDownloadAsync(() => page.GetByRole(AriaRole.Button, new() { Name = "Télécharger en SVG" }).ClickAsync());
        Assert.Equal($"qr-{E2EEnvironment.FormSlug}-riom.svg", qr.SuggestedFilename);

        // Mauvais code : message, aucun fichier, et le code saisi n'est pas réaffiché.
        await page.Locator("#code").FillAsync("0000");
        await page.GetByRole(AriaRole.Button, new() { Name = "Générer le flyer (PDF)" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Code d'accès incorrect");
        await Expect(page.Locator("#code")).ToHaveValueAsync(string.Empty);

        await page.Locator("#flyer-site").SelectOptionAsync("Abrest");
        await page.Locator("#code").FillAsync(App.AccessCode);
        var flyer = await page.RunAndWaitForDownloadAsync(() => page.GetByRole(AriaRole.Button, new() { Name = "Générer le flyer (PDF)" }).ClickAsync());
        Assert.Equal($"flyer-{E2EEnvironment.FormSlug}-abrest.pdf", flyer.SuggestedFilename);

        var path = await flyer.PathAsync();
        var content = await File.ReadAllBytesAsync(path);
        Assert.Equal("%PDF-"u8.ToArray(), content[..5]);
    });
}
