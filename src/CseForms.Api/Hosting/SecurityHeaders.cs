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

            await next();
        });
}
