namespace CseForms.Api.Hosting;

/// <summary>
/// En-têtes de sécurité. Les pages n'utilisent que des ressources du même site, sans script
/// ni style en ligne : la CSP n'autorise rien d'autre.
/// </summary>
internal static class SecurityHeaders
{
    public const string PageContentSecurityPolicy =
        "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; " +
        "form-action 'self'; base-uri 'none'; frame-ancestors 'none'";

    public const string ApiContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";

    /// <summary>Page d'aperçu d'un brouillon : seule page autorisée à afficher un cadre, du même site uniquement.</summary>
    public const string PreviewHostContentSecurityPolicy =
        "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; frame-src 'self'; " +
        "form-action 'self'; base-uri 'none'; frame-ancestors 'none'";

    /// <summary>Cadre de l'aperçu : ne peut être affiché que dans une page du même site.</summary>
    public const string PreviewFrameContentSecurityPolicy =
        "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; " +
        "form-action 'none'; base-uri 'none'; frame-ancestors 'self'";

    public static IApplicationBuilder UseApiSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";

            if (context.Request.Path.StartsWithSegments("/api"))
            {
                headers.ContentSecurityPolicy = ApiContentSecurityPolicy;
                headers.CacheControl = "no-store";
            }
            else
            {
                headers.ContentSecurityPolicy = PageContentSecurityPolicy;
            }

            // Pages du back-office : données personnelles, jamais en cache ni indexées.
            if (context.Request.Path.StartsWithSegments("/admin"))
            {
                headers.CacheControl = "no-store";
                headers["X-Robots-Tag"] = "noindex, nofollow";
            }

            await next();
        });
}
