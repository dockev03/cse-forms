namespace CseForms.Api.BackOffice;

/// <summary>
/// Le back-office n'existe qu'en HTTPS : ses cookies sont <c>Secure</c>. Une requête HTTP y est refusée
/// par un 400 explicite, au lieu de l'erreur 500 que lèverait la protection CSRF. Pas de redirection :
/// derrière un reverse proxy non reconnu (<c>ReverseProxy:KnownNetworks</c>), elle bouclerait.
/// </summary>
internal static class BackOfficeHttpsGuard
{
    public const string Message = "Le back-office n'est accessible qu'en HTTPS.";

    public static IApplicationBuilder UseBackOfficeHttpsGuard(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (!context.Request.IsHttps && context.Request.Path.StartsWithSegments("/admin"))
            {
                // Ni IP ni chemin complet : seulement de quoi diagnostiquer une configuration du proxy.
                context.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger(typeof(BackOfficeHttpsGuard))
                    .LogWarning("Requête HTTP vers le back-office refusée : vérifier X-Forwarded-Proto et ReverseProxy:KnownNetworks.");

                await Results.Text(Message, "text/plain; charset=utf-8", statusCode: StatusCodes.Status400BadRequest)
                    .ExecuteAsync(context);
                return;
            }

            await next();
        });
}
