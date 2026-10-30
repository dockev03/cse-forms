using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace CseForms.Api.Hosting;

/// <summary>
/// Limitation de débit par adresse IP sur l'API publique. L'adresse sert uniquement de clé
/// de partition en mémoire : elle n'est ni stockée ni journalisée.
/// </summary>
internal static class RateLimitingSetup
{
    public const string PublicReadPolicy = "public-read";
    public const string PublicSubmitPolicy = "public-submit";

    public static IServiceCollection AddPublicRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        // Valeurs par défaut prévues pour un site dont les salariés partagent la même IP de sortie.
        var read = Read(configuration, "RateLimiting:PublicRead", permitLimit: 120, windowSeconds: 60);
        var submit = Read(configuration, "RateLimiting:PublicSubmit", permitLimit: 30, windowSeconds: 600);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                await ApiErrors
                    .Error(StatusCodes.Status429TooManyRequests, "tooManyRequests", "Trop de tentatives. Réessayez dans quelques minutes.")
                    .ExecuteAsync(context.HttpContext);
            };

            options.AddPolicy(PublicReadPolicy, context => Partition(context, read));
            options.AddPolicy(PublicSubmitPolicy, context => Partition(context, submit));
        });

        return services;
    }

    private static RateLimitPartition<string> Partition(HttpContext context, FixedWindowRateLimiterOptions limits) =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => limits);

    private static FixedWindowRateLimiterOptions Read(IConfiguration configuration, string section, int permitLimit, int windowSeconds) =>
        new()
        {
            PermitLimit = configuration.GetValue($"{section}:PermitLimit", permitLimit),
            Window = TimeSpan.FromSeconds(configuration.GetValue($"{section}:WindowSeconds", windowSeconds)),
            QueueLimit = 0,
            AutoReplenishment = true,
        };
}
