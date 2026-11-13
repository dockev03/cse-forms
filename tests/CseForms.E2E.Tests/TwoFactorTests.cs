using System.Text.RegularExpressions;
using CseForms.E2E.Tests.Infrastructure;
using CseForms.Testing;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace CseForms.E2E.Tests;

/// <summary>Double authentification : activation par QR code, connexion avec code, appareil de confiance.</summary>
public sealed partial class TwoFactorTests(E2EEnvironment app) : E2ETestBase(app)
{
    [E2EFact]
    public Task Elected_member_enables_two_factor_then_logs_in_with_a_code() => RunAsync(Desktop, async page =>
    {
        var errors = WatchBrowserErrors(page);
        var account = App.TwoFactorUser;
        await LoginAsync(page, account);

        await page.GetByRole(AriaRole.Link, new() { Name = "Sécurité du compte" }).ClickAsync();
        var qr = page.GetByRole(AriaRole.Img, new() { Name = "QR code de configuration de l'application d'authentification" });
        await Expect(qr).ToBeVisibleAsync();
        Assert.True(await qr.EvaluateAsync<int>("img => img.naturalWidth") > 0, "QR code non affiché (CSP ?)");

        var key = (await page.Locator(".key").InnerTextAsync()).Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        await page.Locator("#code-activer").FillAsync(Totp.Code(key));
        await page.GetByRole(AriaRole.Button, new() { Name = "Activer" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Status)).ToHaveTextAsync("Double authentification activée.");
        await Expect(page.Locator(".codes li")).ToHaveCountAsync(10);

        await page.GetByRole(AriaRole.Button, new() { Name = "Se déconnecter" }).ClickAsync();
        await page.GotoAsync("/admin/connexion");
        await SubmitLoginAsync(page, account.Email, account.Password);
        await Expect(page).ToHaveURLAsync(CodeUrl());

        await page.GetByLabel("Code à 6 chiffres de votre application").FillAsync(Totp.Code(key));
        await page.GetByLabel("Faire confiance à cet appareil pendant 30 jours").CheckAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Valider" }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Se déconnecter" })).ToBeVisibleAsync();

        // Appareil de confiance : le mot de passe suffit à la connexion suivante.
        await page.GetByRole(AriaRole.Button, new() { Name = "Se déconnecter" }).ClickAsync();
        await LoginAsync(page, account);

        Assert.Empty(errors);
    });

    [GeneratedRegex("/admin/connexion/code")]
    private static partial Regex CodeUrl();
}
