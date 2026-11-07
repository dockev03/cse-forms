using System.Text.Json;
using CseForms.Application.Common;
using CseForms.Application.FormDefinitions;
using CseForms.Application.Notifications;
using CseForms.Domain.Forms;
using CseForms.Domain.Jobs;
using Microsoft.Extensions.Logging;

namespace CseForms.Application.PublicForms;

/// <summary>Cas d'usage du formulaire public : lecture de la version publiée et soumission d'une réponse.</summary>
public sealed partial class PublicFormService(
    IPublicFormStore store,
    IAccessCodeHasher accessCodeHasher,
    CseCalendar calendar,
    ILogger<PublicFormService> logger)
{
    public const int AccessCodeMaxLength = 64;

    public async Task<PublicFormView?> GetAsync(string slug, CancellationToken cancellationToken)
    {
        var published = await store.FindPublishedAsync(slug, cancellationToken);
        if (published is null)
        {
            return null;
        }

        var (form, version, responseCount) = published;
        var accepting = form.IsAcceptingResponses(calendar.UtcNow, responseCount);
        var sites = accepting ? await store.GetActiveSiteLabelsAsync(cancellationToken) : [];

        return new PublicFormView(
            form.Slug,
            form.Title,
            version.Number,
            accepting,
            form.AccessCodeHash is not null,
            accepting ? version.SchemaJson : null,
            accepting ? version.UiJson : null,
            sites.Order(StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("fr-FR"), ignoreCase: true)).ToArray());
    }

    public async Task<SubmitOutcome> SubmitAsync(string slug, SubmitCommand command, CancellationToken cancellationToken)
    {
        var published = await store.FindPublishedAsync(slug, cancellationToken);
        if (published is null)
        {
            return new SubmitOutcome.NotFound();
        }

        var (form, version, responseCount) = published;
        if (!form.IsAcceptingResponses(calendar.UtcNow, responseCount))
        {
            return new SubmitOutcome.NotAccepting();
        }

        if (command.VersionNumber != version.Number)
        {
            return new SubmitOutcome.VersionChanged();
        }

        if (form.AccessCodeHash is not null && !IsAccessCodeValid(form.AccessCodeHash, command.AccessCode))
        {
            return new SubmitOutcome.AccessDenied();
        }

        var definition = FormDefinitionParser.Parse(version.SchemaJson);
        if (!definition.IsValid)
        {
            // Ne devrait pas arriver : la définition est contrôlée à la publication.
            LogInvalidPublishedDefinition(form.Id, version.Number);
            throw new InvalidOperationException("Définition publiée invalide.");
        }

        var sites = await store.GetActiveSiteLabelsAsync(cancellationToken);
        var validation = SubmissionValidator.Validate(
            definition.Definition!,
            command.Data,
            new ValidationContext(calendar.Today, sites));

        if (!validation.IsValid)
        {
            return new SubmitOutcome.Invalid(validation.Errors);
        }

        var saved = await store.SaveSubmissionAsync(
            new SaveSubmissionCommand(
                form.Id,
                version.Id,
                validation.Data!.ToJsonString(),
                calendar.UtcNow,
                calendar.Today.Year,
                JobsFor(version.SchemaJson, validation.Data!)),
            cancellationToken);

        switch (saved)
        {
            case SaveSubmissionOutcome.Created created:
                LogSubmissionCreated(created.SubmissionId, created.CaseNumber, form.Id);
                return new SubmitOutcome.Created(created.CaseNumber);
            case SaveSubmissionOutcome.VersionChanged:
                return new SubmitOutcome.VersionChanged();
            default:
                return new SubmitOutcome.NotAccepting();
        }
    }

    /// <summary>
    /// Tâches enregistrées avec la réponse : PDF et notification au CSE toujours (la notification ne fait rien
    /// sans règle), accusé de réception seulement si le formulaire en prévoit un et que l'email est renseigné.
    /// </summary>
    private static IReadOnlyList<JobType> JobsFor(string schemaJson, System.Text.Json.Nodes.JsonObject data)
    {
        var receipt = ReceiptSettings.Read(schemaJson);
        var wantsReceipt = receipt is not null && data[receipt.EmailField] is System.Text.Json.Nodes.JsonValue email
            && email.TryGetValue<string>(out var address) && !string.IsNullOrWhiteSpace(address);

        return wantsReceipt ? [JobType.Pdf, JobType.Notification, JobType.Receipt] : [JobType.Pdf, JobType.Notification];
    }

    private bool IsAccessCodeValid(string hash, string? accessCode)
    {
        var code = accessCode?.Trim();
        return !string.IsNullOrEmpty(code)
            && code.Length <= AccessCodeMaxLength
            && accessCodeHasher.Verify(hash, code);
    }

    // Journalisation par identifiants uniquement : aucune donnée saisie.
    [LoggerMessage(Level = LogLevel.Information, Message = "Réponse {SubmissionId} enregistrée (dossier {CaseNumber}, formulaire {FormId})")]
    private partial void LogSubmissionCreated(Guid submissionId, string caseNumber, Guid formId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Définition publiée invalide pour le formulaire {FormId}, version {VersionNumber}")]
    private partial void LogInvalidPublishedDefinition(Guid formId, int versionNumber);
}

/// <summary>
/// Vue publique d'un formulaire. La définition et les sites actifs (pour les champs <c>site</c>)
/// ne sont fournis que si le formulaire accepte des réponses ; le haché du code d'accès n'est jamais exposé.
/// </summary>
public sealed record PublicFormView(
    string Slug,
    string Title,
    int Version,
    bool Accepting,
    bool RequiresAccessCode,
    string? DefinitionJson,
    string? UiJson,
    IReadOnlyList<string> Sites);

public sealed record SubmitCommand(int VersionNumber, string? AccessCode, JsonElement Data);

public abstract record SubmitOutcome
{
    public sealed record Created(string CaseNumber) : SubmitOutcome;

    public sealed record NotFound : SubmitOutcome;

    public sealed record NotAccepting : SubmitOutcome;

    public sealed record VersionChanged : SubmitOutcome;

    public sealed record AccessDenied : SubmitOutcome;

    public sealed record Invalid(IReadOnlyList<ValidationError> Errors) : SubmitOutcome;
}
