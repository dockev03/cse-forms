using System.Net;
using System.Text.RegularExpressions;
using CseForms.Api.BackOffice;
using CseForms.Api.Cli;
using CseForms.Application.BackOffice;
using CseForms.Testing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace CseForms.Api.Tests;

public sealed partial class TwoFactorTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "mot-de-passe-fictif-2026";

    private HttpClient Client() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true,
    });

    private async Task<(int Code, string Output)> RunCliAsync(string stdin, params string[] args)
    {
        var output = new StringWriter();
        var code = await UserCommands.RunAsync(factory.Services, ["users", .. args], new StringReader(stdin), output, interactive: false);
        return (code, output.ToString());
    }

    private async Task<string> CreateUserAsync()
    {
        var email = $"elu-{Guid.NewGuid():N}@example.test";
        Assert.Equal(0, (await RunCliAsync(Password, "create", "--email", email, "--name", "Élu fictif", "--role", AppRoles.Editor)).Code);
        return email;
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string pageUrl, string postUrl, Dictionary<string, string> fields)
    {
        var page = await client.GetStringAsync(pageUrl);
        fields["__RequestVerificationToken"] = TokenPattern().Match(page).Groups[1].Value;
        return await client.PostAsync(postUrl, new FormUrlEncodedContent(fields));
    }

    private static Task<HttpResponseMessage> PasswordAsync(HttpClient client, string email, string? returnUrl = null)
    {
        var fields = new Dictionary<string, string> { ["Email"] = email, ["Password"] = Password };
        if (returnUrl is not null)
        {
            fields["ReturnUrl"] = returnUrl;
        }

        return PostAsync(client, "/admin/connexion", "/admin/connexion", fields);
    }

    private static Task<HttpResponseMessage> CodeAsync(HttpClient client, string code, bool rememberDevice = false, string? returnUrl = null)
    {
        var fields = new Dictionary<string, string> { ["Code"] = code, ["RememberDevice"] = rememberDevice ? "true" : "false" };
        if (returnUrl is not null)
        {
            fields["ReturnUrl"] = returnUrl;
        }

        return PostAsync(client, "/admin/connexion/code", "/admin/connexion/code", fields);
    }

    private static Task<HttpResponseMessage> SecurityAsync(HttpClient client, string handler, string code) =>
        PostAsync(client, "/admin/securite", $"/admin/securite?handler={handler}", new Dictionary<string, string> { ["Code"] = code });

    /// <summary>Connexion par mot de passe, activation de la 2FA ; renvoie la clé et les codes de secours.</summary>
    private async Task<(string Email, string Key, List<string> RecoveryCodes)> UserWithTwoFactorAsync()
    {
        var email = await CreateUserAsync();
        var client = Client();
        Assert.Equal(HttpStatusCode.Redirect, (await PasswordAsync(client, email)).StatusCode);

        var page = await client.GetStringAsync("/admin/securite");
        var key = KeyPattern().Match(page).Groups[1].Value.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        Assert.NotEmpty(key);
        Assert.Contains("src=\"data:image/svg", page, StringComparison.Ordinal);

        var enabled = await (await SecurityAsync(client, "Enable", Totp.Code(key))).Content.ReadAsStringAsync();
        Assert.Contains("Double authentification activée", enabled, StringComparison.Ordinal);
        var codes = RecoveryCodePattern().Matches(enabled).Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(TwoFactorCodes.RecoveryCodeCount, codes.Count);
        return (email, key, codes);
    }

    private async Task<List<string>> AuditActionsAsync(string email)
    {
        await using var db = factory.Database.CreateContext();
        var userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
        return await db.AuditLog
            .Where(a => a.UserId == userId || a.Target == $"user:{userId}")
            .OrderBy(a => a.OccurredAt)
            .Select(a => a.Action)
            .ToListAsync();
    }

    [Fact]
    public async Task Activation_requires_a_valid_code_and_shows_recovery_codes_once()
    {
        var email = await CreateUserAsync();
        var client = Client();
        await PasswordAsync(client, email);

        var first = await client.GetStringAsync("/admin/securite");
        var again = await client.GetStringAsync("/admin/securite");
        Assert.Equal(KeyPattern().Match(first).Groups[1].Value, KeyPattern().Match(again).Groups[1].Value);

        var wrong = await (await SecurityAsync(client, "Enable", "000000")).Content.ReadAsStringAsync();
        Assert.Contains("Code incorrect", wrong, StringComparison.Ordinal);

        var key = KeyPattern().Match(first).Groups[1].Value.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        var enabled = await (await SecurityAsync(client, "Enable", Totp.Code(key))).Content.ReadAsStringAsync();
        Assert.Equal(TwoFactorCodes.RecoveryCodeCount, RecoveryCodePattern().Matches(enabled).Count);

        var later = await client.GetStringAsync("/admin/securite");
        Assert.Empty(RecoveryCodePattern().Matches(later));
        Assert.Contains($"Codes de secours restants : {TwoFactorCodes.RecoveryCodeCount}", later, StringComparison.Ordinal);
        Assert.Contains(AuditActions.TwoFactorEnabled, await AuditActionsAsync(email));
    }

    [Fact]
    public async Task Login_asks_for_the_code_after_the_password_and_keeps_the_deep_link()
    {
        var (email, key, _) = await UserWithTwoFactorAsync();
        var client = Client();

        var password = await PasswordAsync(client, email, "/admin/reponses/00000000-0000-0000-0000-000000000001");
        Assert.Equal(HttpStatusCode.Redirect, password.StatusCode);
        Assert.StartsWith("/admin/connexion/code", password.Headers.Location!.ToString(), StringComparison.Ordinal);

        // Mot de passe seul : toujours pas connecté.
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/admin")).StatusCode);

        var wrong = await CodeAsync(client, "000000", returnUrl: "/admin/securite");
        Assert.Contains("Code incorrect", await wrong.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var code = await CodeAsync(client, Totp.Code(key), returnUrl: "/admin/securite");
        Assert.Equal("/admin/securite", code.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/admin")).StatusCode);

        var audit = await AuditActionsAsync(email);
        Assert.Contains(AuditActions.LoginTwoFactorFailed, audit);
        Assert.Equal(AuditActions.LoginSucceeded, audit[^1]);
    }

    [Fact]
    public async Task Code_page_without_password_step_goes_back_to_login()
    {
        var response = await Client().GetAsync("/admin/connexion/code");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/admin/connexion", response.Headers.Location!.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("/code", response.Headers.Location!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Recovery_code_works_once_whatever_its_case_and_dash()
    {
        var (email, _, codes) = await UserWithTwoFactorAsync();
        var typed = codes[0].Replace("-", " ", StringComparison.Ordinal).ToLowerInvariant();

        var client = Client();
        await PasswordAsync(client, email);
        Assert.Equal(HttpStatusCode.Redirect, (await CodeAsync(client, typed)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/admin")).StatusCode);
        Assert.Contains($"Codes de secours restants : {TwoFactorCodes.RecoveryCodeCount - 1}", await client.GetStringAsync("/admin/securite"), StringComparison.Ordinal);

        var reuse = Client();
        await PasswordAsync(reuse, email);
        Assert.Equal(HttpStatusCode.OK, (await CodeAsync(reuse, codes[0])).StatusCode);
        Assert.Contains(AuditActions.LoginRecoveryCodeUsed, await AuditActionsAsync(email));
    }

    [Fact]
    public async Task Trusted_device_skips_the_code_until_two_factor_is_reset()
    {
        var (email, key, _) = await UserWithTwoFactorAsync();
        var client = Client();
        await PasswordAsync(client, email);
        await CodeAsync(client, Totp.Code(key), rememberDevice: true);
        await PostAsync(client, "/admin", "/admin/deconnexion", []);

        var trusted = await PasswordAsync(client, email);
        Assert.Equal("/admin", trusted.Headers.Location!.ToString());

        Assert.Equal(0, (await RunCliAsync(string.Empty, "reset-2fa", "--email", email)).Code);
        await PostAsync(client, "/admin", "/admin/deconnexion", []);

        // Après réinitialisation : plus de code demandé, l'appareil de confiance ne sert plus à rien.
        Assert.Equal("/admin", (await PasswordAsync(Client(), email)).Headers.Location!.ToString());
        var (_, list) = await RunCliAsync(string.Empty, "list");
        Assert.Contains($"{email} | Élu fictif | {AppRoles.Editor} | sans 2FA", list, StringComparison.Ordinal);
        Assert.Contains(AuditActions.TwoFactorReset, await AuditActionsAsync(email));
    }

    [Fact]
    public async Task Repeated_wrong_codes_lock_the_account()
    {
        var (email, key, _) = await UserWithTwoFactorAsync();
        var client = Client();
        await PasswordAsync(client, email);

        HttpResponseMessage last = null!;
        for (var i = 0; i < 5; i++)
        {
            last = await CodeAsync(client, "000000");
        }

        Assert.Contains("bloqué", await last.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Contains("bloqué", await (await CodeAsync(client, Totp.Code(key))).Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Contains(AuditActions.LoginLockedOut, await AuditActionsAsync(email));
    }

    [Fact]
    public async Task Disabling_and_renewing_codes_require_a_valid_code()
    {
        var (email, key, oldCodes) = await UserWithTwoFactorAsync();
        var client = Client();
        await PasswordAsync(client, email);
        await CodeAsync(client, Totp.Code(key));

        var renewed = await (await SecurityAsync(client, "RecoveryCodes", Totp.Code(key))).Content.ReadAsStringAsync();
        var newCodes = RecoveryCodePattern().Matches(renewed).Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(TwoFactorCodes.RecoveryCodeCount, newCodes.Count);
        Assert.Empty(newCodes.Intersect(oldCodes));

        var refused = await (await SecurityAsync(client, "Disable", "000000")).Content.ReadAsStringAsync();
        Assert.Contains("Code incorrect", refused, StringComparison.Ordinal);
        Assert.Contains("Désactiver la double authentification", refused, StringComparison.Ordinal);

        var disabled = await (await SecurityAsync(client, "Disable", Totp.Code(key))).Content.ReadAsStringAsync();
        Assert.Contains("Double authentification désactivée", disabled, StringComparison.Ordinal);
        Assert.Equal("/admin", (await PasswordAsync(Client(), email)).Headers.Location!.ToString());

        var audit = await AuditActionsAsync(email);
        Assert.Contains(AuditActions.TwoFactorRecoveryCodesRenewed, audit);
        Assert.Contains(AuditActions.TwoFactorDisabled, audit);
    }

    [Theory]
    [InlineData("123456", "123456")]
    [InlineData(" 123 456 ", "123456")]
    [InlineData("123-456", "123456")]
    [InlineData("12345", null)]
    [InlineData("12345a", null)]
    [InlineData(null, null)]
    public void Authenticator_code_is_normalised(string? input, string? expected) =>
        Assert.Equal(expected, TwoFactorCodes.AuthenticatorCode(input));

    [Theory]
    [InlineData("ABCDE-FGH23", "ABCDE-FGH23")]
    [InlineData("abcde fgh23", "ABCDE-FGH23")]
    [InlineData("abcdefgh23", "ABCDE-FGH23")]
    [InlineData("123456", null)]
    [InlineData("ABCDE-FGH2!", null)]
    public void Recovery_code_is_normalised(string input, string? expected) =>
        Assert.Equal(expected, TwoFactorCodes.RecoveryCode(input));

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();

    [GeneratedRegex("<span class=\"key\">([^<]+)</span>")]
    private static partial Regex KeyPattern();

    [GeneratedRegex("<li>([A-Z0-9]{5}-[A-Z0-9]{5})</li>")]
    private static partial Regex RecoveryCodePattern();
}
