using System.Net;
using System.Text.RegularExpressions;
using CseForms.Api.Cli;
using CseForms.Application.BackOffice;
using CseForms.Infrastructure.Identity;
using CseForms.Testing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CseForms.Api.Tests;

/// <summary>Gestion des comptes depuis le back-office : création, mot de passe provisoire, désactivation, garde-fous.</summary>
public sealed partial class AccountManagementTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Password = "mot-de-passe-fictif-2026";
    private const string NewPassword = "nouvelle-phrase-fictive-2026";

    private HttpClient Client() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        AllowAutoRedirect = false,
        HandleCookies = true,
    });

    private async Task<string> CliCreateAsync(string role)
    {
        var email = $"{role}-{Guid.NewGuid():N}@example.test";
        var output = new StringWriter();
        var code = await UserCommands.RunAsync(
            factory.Services,
            ["users", "create", "--email", email, "--name", "Élu fictif", "--role", role],
            new StringReader(Password),
            output,
            interactive: false);
        Assert.Equal(0, code);
        return email;
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string pageUrl, string postUrl, IEnumerable<KeyValuePair<string, string>> fields)
    {
        var page = await client.GetStringAsync(pageUrl);
        var all = fields.Append(new("__RequestVerificationToken", TokenPattern().Match(page).Groups[1].Value));
        return await client.PostAsync(postUrl, new FormUrlEncodedContent(all));
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password) =>
        PostAsync(client, "/admin/connexion", "/admin/connexion", [new("Email", email), new("Password", password)]);

    private async Task<HttpClient> AdminAsync()
    {
        var client = Client();
        Assert.Equal(HttpStatusCode.Redirect, (await LoginAsync(client, await CliCreateAsync(AppRoles.Admin), Password)).StatusCode);
        return client;
    }

    private static Task<HttpResponseMessage> ChangePasswordAsync(HttpClient client, string current, string next, string? confirmation = null) =>
        PostAsync(client, "/admin/mot-de-passe", "/admin/mot-de-passe",
            [new("CurrentPassword", current), new("NewPassword", next), new("Confirmation", confirmation ?? next)]);

    private static Task<HttpResponseMessage> AccountActionAsync(HttpClient client, Guid id, string handler, params KeyValuePair<string, string>[] fields) =>
        PostAsync(client, $"/admin/comptes/{id}", $"/admin/comptes/{id}?handler={handler}", fields);

    /// <summary>Crée un compte depuis l'écran et renvoie son identifiant et son mot de passe provisoire.</summary>
    private static async Task<(Guid Id, string TemporaryPassword)> CreateFromUiAsync(HttpClient admin, string email, string role, params Guid[] formIds)
    {
        var fields = new List<KeyValuePair<string, string>> { new("Email", email), new("DisplayName", "Élu créé à l'écran"), new("Role", role) };
        fields.AddRange(formIds.Select(id => new KeyValuePair<string, string>("FormIds", id.ToString())));
        var response = await PostAsync(admin, "/admin/comptes/nouveau", "/admin/comptes/nouveau", fields);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var password = TemporaryPasswordPattern().Match(html).Groups[1].Value;
        Assert.NotEmpty(password);
        return (Guid.Parse(AccountLinkPattern().Match(html).Groups[1].Value), password);
    }

    private async Task<AppUser> UserAsync(string email)
    {
        await using var db = factory.Database.CreateContext();
        return await db.Users.SingleAsync(u => u.Email == email);
    }

    private async Task<List<string>> AuditAsync(Guid userId)
    {
        await using var db = factory.Database.CreateContext();
        return await db.AuditLog.Where(a => a.Target == $"user:{userId}").OrderBy(a => a.OccurredAt).Select(a => a.Action).ToListAsync();
    }

    [Theory]
    [InlineData(AppRoles.Editor)]
    [InlineData(AppRoles.Reader)]
    public async Task Account_pages_are_hidden_from_non_admins(string role)
    {
        var client = Client();
        await LoginAsync(client, await CliCreateAsync(role), Password);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/admin/comptes")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/admin/comptes/nouveau")).StatusCode);
        Assert.DoesNotContain("/admin/comptes", await client.GetStringAsync("/admin"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Account_pages_require_login()
    {
        var response = await Client().GetAsync("/admin/comptes");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/admin/connexion", response.Headers.Location!.PathAndQuery, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Created_account_must_change_its_temporary_password_before_anything_else()
    {
        Guid formId;
        await using (var db = factory.Database.CreateContext())
        {
            formId = (await TestData.AddPublishedFormAsync(db, $"comptes-{Guid.NewGuid():N}"[..20])).Id;
        }

        var admin = await AdminAsync();
        var email = $"nouveau-{Guid.NewGuid():N}@example.test";
        var (id, temporary) = await CreateFromUiAsync(admin, email, AppRoles.Editor, formId);
        Assert.Matches("^[a-z2-9]{4}(-[a-z2-9]{4}){3}$", temporary);

        var list = await admin.GetStringAsync("/admin/comptes");
        Assert.Contains(email, list, StringComparison.Ordinal);
        Assert.Contains("Mot de passe provisoire", list, StringComparison.Ordinal);
        Assert.DoesNotContain(temporary, list, StringComparison.Ordinal);

        var elu = Client();
        Assert.Equal(HttpStatusCode.Redirect, (await LoginAsync(elu, email, temporary)).StatusCode);
        var blocked = await elu.GetAsync("/admin");
        Assert.Equal("/admin/mot-de-passe", blocked.Headers.Location!.ToString());
        Assert.Equal("/admin/mot-de-passe", (await elu.GetAsync($"/admin/formulaires/{formId}/reponses")).Headers.Location!.ToString());
        Assert.Contains("Votre mot de passe est provisoire", await elu.GetStringAsync("/admin/mot-de-passe"), StringComparison.Ordinal);

        var changed = await ChangePasswordAsync(elu, temporary, NewPassword);
        Assert.Equal(HttpStatusCode.Redirect, changed.StatusCode);
        Assert.Equal("/admin", changed.Headers.Location!.ToString());

        var home = await elu.GetAsync("/admin");
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        Assert.Contains($"/admin/formulaires/{formId}/reponses", await home.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.False((await UserAsync(email)).MustChangePassword);
        Assert.Equal([AuditActions.UserCreated], await AuditAsync(id));
    }

    [Fact]
    public async Task Password_change_checks_confirmation_current_password_and_length()
    {
        var email = await CliCreateAsync(AppRoles.Reader);
        var client = Client();
        await LoginAsync(client, email, Password);

        Assert.Contains("diffèrent", await (await ChangePasswordAsync(client, Password, NewPassword, "autre-saisie-fictive")).Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Contains("doit être différent", await (await ChangePasswordAsync(client, Password, Password)).Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Contains("Mot de passe actuel incorrect", await (await ChangePasswordAsync(client, "mauvais-mot-de-passe", NewPassword)).Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Contains("au moins 12 caractères", await (await ChangePasswordAsync(client, Password, "court")).Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var ok = await ChangePasswordAsync(client, Password, NewPassword);
        Assert.Contains("Mot de passe changé", await ok.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, (await LoginAsync(Client(), email, NewPassword)).StatusCode);
    }

    [Fact]
    public async Task Duplicate_or_invalid_email_is_refused_in_french()
    {
        var admin = await AdminAsync();
        var existing = await CliCreateAsync(AppRoles.Reader);

        var duplicate = await PostAsync(admin, "/admin/comptes/nouveau", "/admin/comptes/nouveau",
            [new("Email", existing), new("DisplayName", "Doublon"), new("Role", AppRoles.Reader)]);
        Assert.Contains("Un compte existe déjà avec cette adresse email.", await duplicate.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var invalid = await PostAsync(admin, "/admin/comptes/nouveau", "/admin/comptes/nouveau",
            [new("Email", "pas-une-adresse"), new("DisplayName", "Invalide"), new("Role", "superadmin")]);
        Assert.Contains("Rôle inconnu", await invalid.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Disabled_account_cannot_log_in_until_enabled()
    {
        var admin = await AdminAsync();
        var email = await CliCreateAsync(AppRoles.Editor);
        var id = (await UserAsync(email)).Id;

        var disabled = await AccountActionAsync(admin, id, "Disable");
        Assert.Contains("Compte désactivé", await disabled.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // Même message qu'un mauvais mot de passe : l'état du compte n'est pas révélé.
        var refused = await LoginAsync(Client(), email, Password);
        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        Assert.Contains("Identifiant ou mot de passe incorrect", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        await AccountActionAsync(admin, id, "Enable");
        Assert.Equal(HttpStatusCode.Redirect, (await LoginAsync(Client(), email, Password)).StatusCode);
        Assert.Equal([AuditActions.UserDisabled, AuditActions.UserEnabled], await AuditAsync(id));
    }

    [Fact]
    public async Task Admin_cannot_disable_or_demote_their_own_account()
    {
        var adminEmail = await CliCreateAsync(AppRoles.Admin);
        var admin = Client();
        await LoginAsync(admin, adminEmail, Password);
        var id = (await UserAsync(adminEmail)).Id;

        var page = await admin.GetStringAsync($"/admin/comptes/{id}");
        Assert.DoesNotContain("Désactiver le compte", page, StringComparison.Ordinal);

        Assert.Contains(AccountAdministration.SelfChangeRefused, await (await AccountActionAsync(admin, id, "Disable")).Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var demote = await AccountActionAsync(admin, id, "Save", new("DisplayName", "Élu fictif"), new("Role", AppRoles.Reader));
        Assert.Contains(AccountAdministration.SelfChangeRefused, await demote.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        Assert.Null((await UserAsync(adminEmail)).DisabledAt);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/admin/comptes")).StatusCode);
    }

    [Fact]
    public async Task Role_and_forms_are_updated_from_the_account_page()
    {
        Guid formId;
        await using (var db = factory.Database.CreateContext())
        {
            formId = (await TestData.AddPublishedFormAsync(db, $"droits-{Guid.NewGuid():N}"[..20])).Id;
        }

        var admin = await AdminAsync();
        var email = await CliCreateAsync(AppRoles.Reader);
        var id = (await UserAsync(email)).Id;

        var saved = await AccountActionAsync(admin, id, "Save", new("DisplayName", "Nom modifié"), new("Role", AppRoles.Editor), new("FormIds", formId.ToString()));
        Assert.Contains("Compte enregistré", await saved.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        await using (var db = factory.Database.CreateContext())
        {
            Assert.True(await db.UserFormAccess.AnyAsync(a => a.UserId == id && a.FormId == formId));
        }

        var elu = Client();
        await LoginAsync(elu, email, Password);
        Assert.Equal(HttpStatusCode.OK, (await elu.GetAsync($"/admin/formulaires/{formId}/diffusion")).StatusCode);

        // Retrait du formulaire : l'élu ne le voit plus.
        await AccountActionAsync(admin, id, "Save", new("DisplayName", "Nom modifié"), new("Role", AppRoles.Editor));
        Assert.Equal(HttpStatusCode.NotFound, (await elu.GetAsync($"/admin/formulaires/{formId}/reponses")).StatusCode);
        Assert.Equal("Nom modifié", (await UserAsync(email)).DisplayName);
    }

    [Fact]
    public async Task Password_reset_issues_a_new_temporary_password_and_unlocks_the_account()
    {
        var admin = await AdminAsync();
        var email = await CliCreateAsync(AppRoles.Editor);
        var id = (await UserAsync(email)).Id;

        for (var i = 0; i < 5; i++)
        {
            await LoginAsync(Client(), email, "mauvais-mot-de-passe");
        }

        var reset = await (await AccountActionAsync(admin, id, "ResetPassword")).Content.ReadAsStringAsync();
        var temporary = TemporaryPasswordPattern().Match(reset).Groups[1].Value;
        Assert.NotEmpty(temporary);
        Assert.DoesNotContain(temporary, await admin.GetStringAsync($"/admin/comptes/{id}"), StringComparison.Ordinal);

        Assert.Contains("incorrect", await (await LoginAsync(Client(), email, Password)).Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var elu = Client();
        Assert.Equal(HttpStatusCode.Redirect, (await LoginAsync(elu, email, temporary)).StatusCode);
        Assert.Equal("/admin/mot-de-passe", (await elu.GetAsync("/admin")).Headers.Location!.ToString());
        Assert.Contains(AuditActions.UserPasswordReset, await AuditAsync(id));
    }

    [Fact]
    public async Task Cli_reset_password_prints_a_temporary_password()
    {
        var email = await CliCreateAsync(AppRoles.Admin);
        var output = new StringWriter();
        Assert.Equal(0, await UserCommands.RunAsync(factory.Services, ["users", "reset-password", "--email", email], new StringReader(string.Empty), output, interactive: false));

        var temporary = Regex.Match(output.ToString(), "([a-z2-9]{4}(?:-[a-z2-9]{4}){3})").Groups[1].Value;
        var client = Client();
        Assert.Equal(HttpStatusCode.Redirect, (await LoginAsync(client, email, temporary)).StatusCode);
        Assert.True((await UserAsync(email)).MustChangePassword);
    }

    [Fact]
    public async Task Cli_errors_are_in_french()
    {
        var output = new StringWriter();
        await UserCommands.RunAsync(
            factory.Services,
            ["users", "create", "--email", $"court-{Guid.NewGuid():N}@example.test", "--name", "X", "--role", AppRoles.Reader],
            new StringReader("court"),
            output,
            interactive: false);
        Assert.Contains("au moins 12 caractères", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Temporary_passwords_are_random_and_long_enough()
    {
        var passwords = Enumerable.Range(0, 50).Select(_ => AccountAdministration.GenerateTemporaryPassword()).ToList();
        Assert.Equal(50, passwords.Distinct().Count());
        Assert.All(passwords, p => Assert.Equal(19, p.Length));
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();

    [GeneratedRegex("<p class=\"key\">([^<]+)</p>")]
    private static partial Regex TemporaryPasswordPattern();

    [GeneratedRegex("href=\"/admin/comptes/([0-9a-f-]{36})\"")]
    private static partial Regex AccountLinkPattern();
}

/// <summary>API démarrée avec un administrateur initial dans la configuration, sur une base vide.</summary>
public sealed class BootstrapApiFactory : ApiFactory
{
    public const string Email = "admin-initial@example.test";
    public const string Password = "phrase-initiale-fictive-2026";

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("BootstrapAdmin:Email", Email);
        builder.UseSetting("BootstrapAdmin:Password", Password);
    }
}

public sealed partial class BootstrapAdminTests(BootstrapApiFactory factory) : IClassFixture<BootstrapApiFactory>
{
    [Fact]
    public async Task First_start_creates_a_single_admin_who_must_change_the_password()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true,
        });

        var login = await client.GetStringAsync("/admin/connexion");
        var token = TokenPattern().Match(login).Groups[1].Value;
        var response = await client.PostAsync("/admin/connexion", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = BootstrapApiFactory.Email,
            ["Password"] = BootstrapApiFactory.Password,
            ["__RequestVerificationToken"] = token,
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/admin/mot-de-passe", (await client.GetAsync("/admin/comptes")).Headers.Location!.ToString());

        // Second démarrage : un compte existe, rien n'est créé ni modifié.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<AccountAdministration>();
            Assert.False(await accounts.BootstrapAdminAsync("autre@example.test", null, "autre-phrase-fictive-2026", CancellationToken.None));
        }

        await using var db = factory.Database.CreateContext();
        var admin = await db.Users.SingleAsync();
        Assert.Equal(BootstrapApiFactory.Email, admin.Email);
        Assert.True(admin.MustChangePassword);
        Assert.Contains(await db.AuditLog.Select(a => a.Action).ToListAsync(), a => a == AuditActions.BootstrapAdminCreated);
    }

    [Fact]
    public async Task The_last_active_admin_cannot_be_disabled_or_demoted()
    {
        // Démarre l'API (et donc la création de l'administrateur initial) avant de lire la base.
        _ = factory.Services;
        Guid adminId;
        await using (var db = factory.Database.CreateContext())
        {
            adminId = await db.Users.Where(u => u.Email == BootstrapApiFactory.Email).Select(u => u.Id).SingleAsync();
        }

        await using var scope = factory.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountAdministration>();
        var someoneElse = Guid.NewGuid();

        Assert.Equal([AccountAdministration.LastAdminRefused], (await accounts.SetDisabledAsync(someoneElse, adminId, disabled: true, CancellationToken.None)).Errors);
        Assert.Equal([AccountAdministration.LastAdminRefused], (await accounts.UpdateAsync(someoneElse, adminId, "Administrateur", AppRoles.Editor, [], CancellationToken.None)).Errors);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenPattern();
}
