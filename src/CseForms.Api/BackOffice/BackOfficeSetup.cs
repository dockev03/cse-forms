using CseForms.Application.BackOffice;
using CseForms.Infrastructure.Identity;
using CseForms.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.WebEncoders;
using System.Text.Encodings.Web;
using System.Text.Unicode;

namespace CseForms.Api.BackOffice;

/// <summary>
/// Authentification et autorisation du back-office : comptes nominatifs, rôles, session courte,
/// cookies <c>Secure</c>, <c>HttpOnly</c>, <c>SameSite=Strict</c>, protection CSRF.
/// </summary>
internal static class BackOfficeSetup
{
    public const string Policy = "back-office";
    public const string LoginPath = "/admin/connexion";
    public const string AccessDeniedPath = "/admin/acces-refuse";
    public static readonly TimeSpan SessionTimeout = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan TrustedDeviceDuration = TimeSpan.FromDays(30);

    public static IServiceCollection AddBackOffice(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddIdentity<AppUser, AppRole>(options =>
            {
                // Longueur plutôt que règles de composition (recommandations ANSSI et NIST).
                options.Password.RequiredLength = 12;
                options.Password.RequiredUniqueChars = 4;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;

                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;

                options.User.RequireUniqueEmail = true;

                // « Compte confirmé » = compte actif : un compte désactivé ne peut plus se connecter.
                options.SignIn.RequireConfirmedAccount = true;
            })
            .AddEntityFrameworkStores<CseFormsDbContext>()
            .AddDefaultTokenProviders()
            .AddErrorDescriber<FrenchIdentityErrorDescriber>()
            .AddClaimsPrincipalFactory<AppUserClaimsPrincipalFactory>()
            .AddUserConfirmation<ActiveAccountConfirmation>();

        services.AddScoped<AccountAdministration>();

        // Un compte désactivé ou dont le mot de passe change perd sa session rapidement.
        services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.FromMinutes(5));

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "__Host-cse-session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.ExpireTimeSpan = SessionTimeout;
            options.SlidingExpiration = true;
            options.LoginPath = LoginPath;
            options.LogoutPath = "/admin/deconnexion";
            // Pas la page de connexion : elle renvoie un compte connecté vers la page refusée, en boucle.
            options.AccessDeniedPath = AccessDeniedPath;
            options.ReturnUrlParameter = "returnUrl";
        });

        // Étape intermédiaire entre le mot de passe et le code : 5 minutes pour saisir le code.
        services.Configure<CookieAuthenticationOptions>(IdentityConstants.TwoFactorUserIdScheme, options =>
        {
            options.Cookie.Name = "__Host-cse-2fa";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
        });

        // Appareil de confiance : plus de code pendant 30 jours sur ce navigateur. Invalidé quand la 2FA
        // est désactivée ou réinitialisée (changement du jeton de sécurité du compte).
        services.Configure<CookieAuthenticationOptions>(IdentityConstants.TwoFactorRememberMeScheme, options =>
        {
            options.Cookie.Name = "__Host-cse-device";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.ExpireTimeSpan = TrustedDeviceDuration;
        });

        services.AddAntiforgery(options =>
        {
            options.Cookie.Name = "__Host-cse-af";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
        });

        // Clés des cookies : à conserver sur un volume en production, sinon les sessions tombent au redémarrage.
        if (configuration["DataProtection:KeysPath"] is { Length: > 0 } keysPath)
        {
            services.AddDataProtection()
                .SetApplicationName("cse-forms")
                .PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        }

        services.AddAuthorizationBuilder()
            .AddPolicy(Policy, policy => policy.RequireAuthenticatedUser().RequireRole(AppRoles.All));
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, AccessDeniedAuditHandler>();

        // Accents affichés tels quels plutôt qu'en entités (&#xE9;) ; l'encodage HTML reste actif.
        services.Configure<WebEncoderOptions>(options => options.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));

        services
            .AddRazorPages(options =>
            {
                options.Conventions.AuthorizeFolder("/Admin", Policy);
                options.Conventions.AllowAnonymousToPage("/Admin/Login");
                options.Conventions.AllowAnonymousToPage("/Admin/LoginCode");
                options.Conventions.AllowAnonymousToPage("/Admin/AccessDenied");
            })
            .AddMvcOptions(options => options.Filters.Add<PasswordChangeFilter>());

        return services;
    }
}
