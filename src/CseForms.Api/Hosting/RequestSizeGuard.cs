using Microsoft.AspNetCore.Http.Metadata;

namespace CseForms.Api.Hosting;

/// <summary>
/// Refuse dès l'en-tête <c>Content-Length</c> un corps plus grand que la limite de l'endpoint.
/// Double la limite de Kestrel (qui couvre aussi les corps envoyés par morceaux) sans lire le corps.
/// </summary>
internal static class RequestSizeGuard
{
    public static IApplicationBuilder UseRequestSizeGuard(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var limit = context.GetEndpoint()?.Metadata.GetMetadata<IRequestSizeLimitMetadata>()?.MaxRequestBodySize;
            if (limit is not null && context.Request.ContentLength > limit)
            {
                await ApiErrors
                    .Error(StatusCodes.Status413PayloadTooLarge, "payloadTooLarge", "Requête trop volumineuse.")
                    .ExecuteAsync(context);
                return;
            }

            await next();
        });
}
