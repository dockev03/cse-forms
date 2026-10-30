using CseForms.Application.BackOffice;
using CseForms.Infrastructure.Identity;
using CseForms.Infrastructure.Persistence;
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
    public static readonly TimeSpan SessionTimeout = TimeSpan.FromMinutes(30);

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
            })
            .AddEntityFrameworkStores<CseFormsDbContext>()
            .AddDefaultTokenProviders();

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
            options.AccessDeniedPath = LoginPath;
            options.ReturnUrlParameter = "returnUrl";
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

        // Accents affichés tels quels plutôt qu'en entités (&#xE9;) ; l'encodage HTML reste actif.
        services.Configure<WebEncoderOptions>(options => options.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));

        services.AddRazorPages(options =>
        {
            options.Conventions.AuthorizeFolder("/Admin", Policy);
            options.Conventions.AllowAnonymousToPage("/Admin/Login");
        });

        return services;
    }
}
