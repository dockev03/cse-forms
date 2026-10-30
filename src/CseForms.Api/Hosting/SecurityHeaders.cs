namespace CseForms.Api.Hosting;

/// <summary>En-têtes de sécurité de base ; la CSP des pages du formulaire public sera ajoutée avec le front.</summary>
internal static class SecurityHeaders
{
    public static IApplicationBuilder UseApiSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";

            if (context.Request.Path.StartsWithSegments("/api"))
            {
                headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
                headers.CacheControl = "no-store";
            }

            await next();
        });
}
