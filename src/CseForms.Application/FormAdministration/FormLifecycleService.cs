using System.Globalization;
using CseForms.Application.BackOffice;
using CseForms.Application.Common;
using CseForms.Domain.Common;
using CseForms.Domain.Forms;

namespace CseForms.Application.FormAdministration;

/// <summary>
/// Cycle de vie d'un formulaire depuis le back-office : fermeture, réouverture, dates d'ouverture et de
/// fermeture, nombre maximal de réponses, changement du code d'accès. Réservé aux administrateurs et aux
/// éditeurs ayant le droit sur le formulaire ; chaque refus et chaque changement sont journalisés.
/// La définition, elle, ne change que par une nouvelle version publiée.
/// </summary>
public sealed class FormLifecycleService(
    IBackOfficeStore backOffice,
    IFormLifecycleStore store,
    IAuditLog audit,
    IAccessCodeHasher accessCodeHasher,
    TimeProvider timeProvider)
{
    public async Task<LifecycleResult<FormLifecyclePage>> GetPageAsync(CurrentUser user, Guid formId, CancellationToken cancellationToken)
    {
        var (status, form) = await AuthorizeAsync(user, formId, cancellationToken);
        if (status != LifecycleStatus.Ok)
        {
            return new(status);
        }

        var responseCount = await store.CountResponsesAsync(formId, cancellationToken);
        return new(LifecycleStatus.Ok, new FormLifecyclePage(
            form!.Id,
            form.Slug,
            form.Title,
            form.Status,
            form.CurrentVersion?.Number,
            form.OpensAt,
            form.ClosesAt,
            form.MaxResponses,
            responseCount,
            form.AccessCodeHash is not null,
            Availability(form, timeProvider.GetUtcNow(), responseCount)));
    }

    public Task<LifecycleResult> CloseAsync(CurrentUser user, Guid formId, CancellationToken cancellationToken) =>
        ChangeStatusAsync(user, formId, form => form.Close(), AuditActions.FormClosed, cancellationToken);

    public Task<LifecycleResult> ReopenAsync(CurrentUser user, Guid formId, CancellationToken cancellationToken) =>
        ChangeStatusAsync(user, formId, form => form.Reopen(), AuditActions.FormReopened, cancellationToken);

    /// <summary>
    /// Dates saisies à l'heure de Paris, stockées en UTC ; null retire la borne. Un plafond inférieur au nombre
    /// de réponses déjà reçues est accepté : le formulaire cesse simplement d'accepter des réponses.
    /// </summary>
    public async Task<LifecycleResult> UpdateScheduleAsync(
        CurrentUser user,
        Guid formId,
        DateTime? opensAtLocal,
        DateTime? closesAtLocal,
        int? maxResponses,
        CancellationToken cancellationToken)
    {
        var (status, form) = await AuthorizeAsync(user, formId, cancellationToken);
        if (status != LifecycleStatus.Ok)
        {
            return new(status);
        }

        DateTimeOffset? opensAt = null;
        DateTimeOffset? closesAt = null;
        if (opensAtLocal is { } o && (opensAt = CseCalendar.LocalToUtc(o)) is null)
        {
            return LifecycleResult.Invalid(MissingHour("d'ouverture"));
        }

        if (closesAtLocal is { } c && (closesAt = CseCalendar.LocalToUtc(c)) is null)
        {
            return LifecycleResult.Invalid(MissingHour("de fermeture"));
        }

        if (opensAt == form!.OpensAt && closesAt == form.ClosesAt && maxResponses == form.MaxResponses)
        {
            return LifecycleResult.Ok;
        }

        try
        {
            form.SetSchedule(opensAt, closesAt);
            form.SetMaxResponses(maxResponses);
        }
        catch (DomainException ex)
        {
            return LifecycleResult.Invalid(ex.Message);
        }

        await store.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(
            AuditActions.FormScheduleChanged,
            user.Id,
            Target(formId),
            $"opens={Format(opensAt)};closes={Format(closesAt)};max={maxResponses?.ToString(CultureInfo.InvariantCulture) ?? "-"}",
            cancellationToken);
        return LifecycleResult.Ok;
    }

    /// <summary>
    /// Nouveau code d'accès, saisi deux fois puisqu'il sera imprimé ; il n'est ni journalisé ni renvoyé à la page.
    /// Un formulaire public reste public : le mode d'accès fait partie de la définition.
    /// </summary>
    public async Task<LifecycleResult> ChangeAccessCodeAsync(
        CurrentUser user,
        Guid formId,
        string? accessCode,
        string? confirmation,
        CancellationToken cancellationToken)
    {
        var (status, form) = await AuthorizeAsync(user, formId, cancellationToken);
        if (status != LifecycleStatus.Ok)
        {
            return new(status);
        }

        if (form!.AccessCodeHash is null)
        {
            return LifecycleResult.Invalid("Ce formulaire est public : il n'a pas de code d'accès.");
        }

        accessCode = accessCode?.Trim();
        if (!FormAdminService.IsValidAccessCode(accessCode))
        {
            return LifecycleResult.Invalid($"Le code d'accès doit comporter {FormAdminService.AccessCodeRule}.");
        }

        if (!string.Equals(accessCode, confirmation?.Trim(), StringComparison.Ordinal))
        {
            return LifecycleResult.Invalid("Les deux saisies du code d'accès sont différentes.");
        }

        form.SetAccessCodeHash(accessCodeHasher.Hash(accessCode!));
        await store.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(AuditActions.FormAccessCodeChanged, user.Id, Target(formId), cancellationToken);
        return LifecycleResult.Ok;
    }

    /// <summary>État effectif, dans l'ordre où le formulaire public le vérifie.</summary>
    public static FormAvailability Availability(Form form, DateTimeOffset now, int responseCount) =>
        form.Status switch
        {
            FormStatus.Closed => FormAvailability.Closed,
            _ when form.Status != FormStatus.Published || form.CurrentVersionId is null => FormAvailability.Draft,
            _ when form.OpensAt is { } opens && now < opens => FormAvailability.NotYetOpen,
            _ when form.ClosesAt is { } closes && now >= closes => FormAvailability.Ended,
            _ when form.MaxResponses is { } max && responseCount >= max => FormAvailability.Full,
            _ => FormAvailability.Open,
        };

    private async Task<LifecycleResult> ChangeStatusAsync(
        CurrentUser user,
        Guid formId,
        Action<Form> change,
        string auditAction,
        CancellationToken cancellationToken)
    {
        var (status, form) = await AuthorizeAsync(user, formId, cancellationToken);
        if (status != LifecycleStatus.Ok)
        {
            return new(status);
        }

        try
        {
            change(form!);
        }
        catch (DomainException ex)
        {
            return LifecycleResult.Invalid(ex.Message);
        }

        await store.SaveChangesAsync(cancellationToken);
        await audit.RecordAsync(auditAction, user.Id, Target(formId), cancellationToken);
        return LifecycleResult.Ok;
    }

    /// <summary>Mêmes règles que la diffusion : 404 sans droit sur le formulaire, 403 pour un lecteur.</summary>
    private async Task<(LifecycleStatus Status, Form? Form)> AuthorizeAsync(CurrentUser user, Guid formId, CancellationToken cancellationToken)
    {
        var form = await store.FindAsync(formId, cancellationToken);
        if (form is null)
        {
            return (LifecycleStatus.NotFound, null);
        }

        if (!await backOffice.CanAccessFormAsync(user, formId, cancellationToken))
        {
            await audit.RecordAsync(AuditActions.AccessDenied, user.Id, Target(formId), cancellationToken);
            return (LifecycleStatus.NotFound, null);
        }

        if (user.IsReadOnly)
        {
            await audit.RecordAsync(AuditActions.AccessDenied, user.Id, Target(formId), cancellationToken);
            return (LifecycleStatus.Forbidden, null);
        }

        return (LifecycleStatus.Ok, form);
    }

    private static string Target(Guid formId) => $"form:{formId}";

    private static string Format(DateTimeOffset? value) =>
        value?.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm'Z'", CultureInfo.InvariantCulture) ?? "-";

    private static string MissingHour(string which) =>
        $"L'heure {which} n'existe pas ce jour-là (passage à l'heure d'été) : choisissez une autre heure.";
}

public interface IFormLifecycleStore
{
    /// <summary>Formulaire suivi pour modification, avec ses versions ; null s'il n'existe pas.</summary>
    Task<Form?> FindAsync(Guid formId, CancellationToken cancellationToken);

    Task<int> CountResponsesAsync(Guid formId, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public enum LifecycleStatus
{
    Ok,
    NotFound,
    Forbidden,
    Invalid,
}

public sealed record LifecycleResult(LifecycleStatus Status, string? Error = null)
{
    public static readonly LifecycleResult Ok = new(LifecycleStatus.Ok);

    public static LifecycleResult Invalid(string error) => new(LifecycleStatus.Invalid, error);
}

public sealed record LifecycleResult<T>(LifecycleStatus Status, T? Value = default);

/// <summary>Ce que voit un répondant qui ouvre le lien public maintenant.</summary>
public enum FormAvailability
{
    Open,
    Draft,
    Closed,
    NotYetOpen,
    Ended,
    Full,
}

/// <summary>Écran « Réglages » : dates en UTC, converties à l'heure de Paris à l'affichage.</summary>
public sealed record FormLifecyclePage(
    Guid FormId,
    string Slug,
    string Title,
    FormStatus Status,
    int? VersionNumber,
    DateTimeOffset? OpensAt,
    DateTimeOffset? ClosesAt,
    int? MaxResponses,
    int ResponseCount,
    bool RequiresAccessCode,
    FormAvailability Availability);
