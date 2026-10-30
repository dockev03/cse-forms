namespace CseForms.Api.Hosting;

/// <summary>
/// Réponses d'erreur sans détail technique : JSON pour l'API, texte simple pour les pages.
/// </summary>
internal static class ErrorResponses
{
    public static Task Write(HttpContext context, int status)
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            return status switch
            {
                StatusCodes.Status400BadRequest => ApiErrors.BadRequest().ExecuteAsync(context),
                < 500 => ApiErrors.Error(status, "badRequest", "Requête invalide.").ExecuteAsync(context),
                _ => ApiErrors.Error(status, "serverError", "Une erreur est survenue. Réessayez plus tard.").ExecuteAsync(context),
            };
        }

        var message = status switch
        {
            StatusCodes.Status400BadRequest => "Requête invalide. Rechargez la page puis réessayez.",
            StatusCodes.Status404NotFound => "Page introuvable.",
            < 500 => "Requête refusée.",
            _ => "Une erreur est survenue. Réessayez plus tard.",
        };

        return Results.Text(message, "text/plain; charset=utf-8", statusCode: status).ExecuteAsync(context);
    }

    /// <summary>Complète une réponse d'erreur restée sans corps (404 d'une page, 400 de liaison...).</summary>
    public static Task WriteIfEmpty(HttpContext context)
    {
        var status = context.Response.StatusCode;
        return status is >= 400 and < 600 ? Write(context, status) : Task.CompletedTask;
    }
}
