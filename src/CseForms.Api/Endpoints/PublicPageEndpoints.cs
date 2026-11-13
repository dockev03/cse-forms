using System.Security.Cryptography;
using System.Text;
using CseForms.Application.Notifications;
using CseForms.Application.Settings;
using Microsoft.Extensions.FileProviders;

namespace CseForms.Api.Endpoints;

/// <summary>
/// Page du formulaire public. Une seule page statique pour tous les formulaires :
/// le script lit le slug dans l'URL et charge la définition par l'API. La personnalisation du CSE
/// (couleur, image de fond) arrive par la feuille <c>/theme.css</c>, le logo par <c>/logo</c>, tous deux
/// tirés des paramètres et servis par le site lui-même (CSP inchangée).
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

        // Couleur et image de fond : rien de personnel. Revalidée à chaque visite (ETag) pour qu'un changement
        // soit visible tout de suite sans retélécharger l'image à chaque page.
        app.MapGet("/theme.css", async (HttpContext context, ISettingReader settings, CancellationToken cancellationToken) =>
        {
            var css = ThemeStylesheet.Render(await settings.GetAllAsync(cancellationToken));
            var etag = Etag(Encoding.UTF8.GetBytes(css));
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.ETag = etag;

            return context.Request.Headers.IfNoneMatch.ToString() == etag
                ? Results.StatusCode(StatusCodes.Status304NotModified)
                : Results.Text(css, "text/css; charset=utf-8");
        });

        // Logo du CSE, déjà affiché sur le flyer : rien de personnel. 404 s'il n'est pas défini ; la page ne le demande alors pas (« logo » de l'API du formulaire).
        app.MapGet("/logo", async (HttpContext context, ISettingReader settings, CancellationToken cancellationToken) =>
        {
            var values = await settings.GetAllAsync(cancellationToken);
            if (CseLogo.Image.Decode(values.GetValueOrDefault(SettingKeys.CseLogo)) is not { } logo)
            {
                return Results.NotFound();
            }

            var (content, mediaType) = logo;
            var etag = Etag(content);
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.ETag = etag;
            return context.Request.Headers.IfNoneMatch.ToString() == etag
                ? Results.StatusCode(StatusCodes.Status304NotModified)
                : Results.File(content, mediaType);
        });

        return app;
    }

    private static string Etag(byte[] content) => $"\"{Convert.ToHexStringLower(SHA256.HashData(content))[..32]}\"";

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
