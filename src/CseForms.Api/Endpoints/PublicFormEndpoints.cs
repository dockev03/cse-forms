using CseForms.Application.Notifications;
using CseForms.Application.Settings;
using System.Text.Json;
using CseForms.Api.Hosting;
using CseForms.Application.ProofOfWork;
using CseForms.Application.PublicForms;
using Microsoft.AspNetCore.Mvc;

namespace CseForms.Api.Endpoints;

/// <summary>API du formulaire public, sans authentification. Les messages ne contiennent aucun détail technique.</summary>
internal static class PublicFormEndpoints
{
    public const long MaxSubmissionBytes = 64 * 1024;

    public static IEndpointRouteBuilder MapPublicFormEndpoints(this IEndpointRouteBuilder app)
    {
        // Défi anti-robot, à résoudre par le navigateur avant l'envoi (preuve de travail, format ALTCHA).
        app.MapGet("/api/public/challenge", (ProofOfWorkService proofOfWork) => Results.Ok(proofOfWork.CreateChallenge()))
            .RequireRateLimiting(RateLimitingSetup.PublicReadPolicy);

        var group = app.MapGroup("/api/public/forms");

        group.MapGet("/{slug}", GetForm)
            .RequireRateLimiting(RateLimitingSetup.PublicReadPolicy);

        group.MapPost("/{slug}/submissions", Submit)
            .RequireRateLimiting(RateLimitingSetup.PublicSubmitPolicy)
            .WithMetadata(new RequestSizeLimitAttribute(MaxSubmissionBytes));

        return app;
    }

    private static async Task<IResult> GetForm(string slug, PublicFormService service, ISettingReader settings, CancellationToken cancellationToken)
    {
        var view = await service.GetAsync(slug, cancellationToken);
        if (view is null)
        {
            return ApiErrors.NotFound();
        }

        return Results.Ok(new PublicFormResponse(
            view.Slug,
            view.Title,
            view.Version,
            view.Accepting,
            view.RequiresAccessCode,
            ParseOrNull(view.DefinitionJson),
            ParseOrNull(view.UiJson),
            view.Sites,
            // La page ne demande /logo que s'il existe : aucune requête en erreur sans logo.
            CseLogo.IsValid((await settings.GetAllAsync(cancellationToken)).GetValueOrDefault(SettingKeys.CseLogo))));
    }

    private static async Task<IResult> Submit(
        string slug,
        SubmitRequest? request,
        PublicFormService service,
        ProofOfWorkService proofOfWork,
        CancellationToken cancellationToken)
    {
        if (request is null || request.Version <= 0 || request.Data.ValueKind != JsonValueKind.Object)
        {
            return ApiErrors.BadRequest();
        }

        // Vérifiée avant tout le reste : un robot sans preuve ne coûte qu'un calcul d'empreinte.
        if (!await proofOfWork.VerifyAsync(request.Proof, cancellationToken))
        {
            return ApiErrors.Forbidden("proofInvalid", "La vérification anti-robot a échoué. Réessayez l'envoi.");
        }

        var outcome = await service.SubmitAsync(
            slug,
            new SubmitCommand(request.Version, request.AccessCode, request.Data),
            cancellationToken);

        return outcome switch
        {
            SubmitOutcome.Created created => Results.Created((string?)null, new SubmitResponse(created.CaseNumber)),
            SubmitOutcome.NotFound => ApiErrors.NotFound(),
            SubmitOutcome.NotAccepting => ApiErrors.Conflict("notAccepting", "Ce formulaire n'accepte plus de réponse."),
            SubmitOutcome.VersionChanged => ApiErrors.Conflict("versionChanged", "Le formulaire a été modifié. Rechargez la page pour continuer."),
            SubmitOutcome.AccessDenied => ApiErrors.Forbidden("accessDenied", "Code d'accès incorrect."),
            SubmitOutcome.Invalid invalid => ApiErrors.Validation(invalid.Errors),
            _ => throw new InvalidOperationException("Résultat de soumission inattendu."),
        };
    }

    private static JsonElement? ParseOrNull(string? json)
    {
        if (json is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    internal sealed record PublicFormResponse(
        string Slug,
        string Title,
        int Version,
        bool Accepting,
        bool RequiresAccessCode,
        JsonElement? Definition,
        JsonElement? Ui,
        IReadOnlyList<string> Sites,
        bool Logo);

    internal sealed record SubmitRequest(int Version, string? AccessCode, JsonElement Data, ProofOfWorkSolution? Proof);

    internal sealed record SubmitResponse(string CaseNumber);
}
