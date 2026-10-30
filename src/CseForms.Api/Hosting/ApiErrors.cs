using CseForms.Application.FormDefinitions;

namespace CseForms.Api.Hosting;

/// <summary>Réponses d'erreur homogènes : un code stable pour le client et un message en français.</summary>
internal static class ApiErrors
{
    public static IResult BadRequest() =>
        Error(StatusCodes.Status400BadRequest, "badRequest", "Requête invalide.");

    public static IResult NotFound() =>
        Error(StatusCodes.Status404NotFound, "notFound", "Formulaire introuvable.");

    public static IResult Conflict(string code, string message) =>
        Error(StatusCodes.Status409Conflict, code, message);

    public static IResult Forbidden(string code, string message) =>
        Error(StatusCodes.Status403Forbidden, code, message);

    public static IResult Validation(IReadOnlyList<ValidationError> errors) =>
        Results.Json(
            new ErrorResponse("invalid", "Certaines réponses sont à corriger.", errors),
            statusCode: StatusCodes.Status422UnprocessableEntity);

    public static IResult Error(int status, string code, string message) =>
        Results.Json(new ErrorResponse(code, message, null), statusCode: status);

    internal sealed record ErrorResponse(string Code, string Message, IReadOnlyList<ValidationError>? Errors);
}
