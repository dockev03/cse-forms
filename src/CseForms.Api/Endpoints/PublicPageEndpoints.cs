using Microsoft.Extensions.FileProviders;

namespace CseForms.Api.Endpoints;

/// <summary>
/// Page du formulaire public. Une seule page statique pour tous les formulaires :
/// le script lit le slug dans l'URL et charge la définition par l'API.
/// </summary>
internal static class PublicPageEndpoints
{
    public static IEndpointRouteBuilder MapPublicPages(this IEndpointRouteBuilder app, IWebHostEnvironment environment)
    {
        var page = environment.WebRootFileProvider.GetFileInfo("form.html");

        app.MapGet("/f/{slug}", (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-cache";
            return Results.Stream(page.CreateReadStream(), "text/html; charset=utf-8");
        });

        return app;
    }

    public static WebApplication UsePublicAssets(this WebApplication app)
    {
        // Seuls les fichiers de /assets sont servis tels quels ; la page passe par /f/{slug}.
        app.UseStaticFiles(new StaticFileOptions
        {
            RequestPath = "/assets",
            FileProvider = new PhysicalFileProvider(Path.Combine(app.Environment.WebRootPath, "assets")),
            OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-cache",
        });

        return app;
    }
}
