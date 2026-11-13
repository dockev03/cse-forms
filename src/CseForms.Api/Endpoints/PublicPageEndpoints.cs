using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using CseForms.Application.Notifications;
using CseForms.Application.Settings;
using Microsoft.Extensions.FileProviders;

namespace CseForms.Api.Endpoints;

/// <summary>
/// Page du formulaire public. Une seule page statique pour tous les formulaires :
/// le script lit le slug dans l'URL et charge la définition par l'API. La personnalisation du CSE
/// (couleur, image de fond) arrive par la feuille <c>/theme.css</c>, le logo par <c>/logo</c>, tous deux
/// tirés des paramètres et servis par le site lui-même (CSP inchangée). La notice RGPD complète
/// (<see cref="PrivacyNotice.PublicPath"/>) est rendue côté serveur depuis le paramètre <c>rgpd.notice</c>, sans script.
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

        // Notice RGPD : texte du CSE, rien de personnel. 404 tant qu'elle n'est pas rédigée ; le formulaire n'affiche alors pas de lien.
        app.MapGet(PrivacyNotice.PublicPath, async (HttpContext context, ISettingReader settings, CancellationToken cancellationToken) =>
        {
            var values = await settings.GetAllAsync(cancellationToken);
            var hasLogo = CseLogo.IsValid(values.GetValueOrDefault(SettingKeys.CseLogo));
            context.Response.Headers.CacheControl = "no-cache";

            return values.TryGetValue(SettingKeys.RgpdNotice, out var notice) && notice.Length > 0
                ? Results.Content(NoticePage(hasLogo, "Vos données personnelles", PrivacyNotice.ToHtml(notice)), "text/html; charset=utf-8")
                : Results.Content(
                    NoticePage(hasLogo, "Notice indisponible", "<p>Le CSE n'a pas encore publié cette notice. Pour toute question sur vos données, contactez le CSE.</p>\n"),
                    "text/html; charset=utf-8",
                    statusCode: StatusCodes.Status404NotFound);
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

    /// <summary>Page autonome, mêmes feuilles de style que le formulaire ; <paramref name="body"/> est du HTML déjà échappé.</summary>
    private static string NoticePage(bool hasLogo, string title, string body)
    {
        var encodedTitle = HtmlEncoder.Create(System.Text.Unicode.UnicodeRanges.All).Encode(title);
        return new StringBuilder()
            .Append("<!doctype html>\n<html lang=\"fr\">\n<head>\n<meta charset=\"utf-8\">\n")
            .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n")
            .Append("<meta name=\"referrer\" content=\"no-referrer\">\n<meta name=\"robots\" content=\"noindex\">\n")
            .Append("<title>").Append(encodedTitle).Append("</title>\n")
            .Append("<link rel=\"stylesheet\" href=\"/assets/form.css\">\n<link rel=\"stylesheet\" href=\"/theme.css\">\n</head>\n<body>\n<div class=\"page\">\n")
            .Append(hasLogo ? "<header class=\"brand\"><img class=\"cse-logo\" src=\"/logo\" alt=\"Logo du CSE\"></header>\n" : string.Empty)
            .Append("<main>\n<h1>").Append(encodedTitle).Append("</h1>\n<article class=\"card privacy-notice\">\n")
            .Append(body)
            .Append("</article>\n</main>\n</div>\n</body>\n</html>\n")
            .ToString();
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
