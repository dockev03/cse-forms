using System.Text.RegularExpressions;
using CseForms.Domain.Common;
using CseForms.Domain.Submissions;

namespace CseForms.Domain.Forms;

public sealed partial class Form
{
    public const int SlugMaxLength = 64;
    public const int TitleMaxLength = 200;
    public const string DefaultCaseNumberPrefix = "DOS";

    private readonly List<FormVersion> _versions = [];

    private Form()
    {
        Slug = null!;
        Title = null!;
        CaseNumberPrefix = null!;
    }

    public Form(string slug, string title, DateTimeOffset createdAt)
    {
        Id = Guid.CreateVersion7();
        Slug = NormalizeSlug(slug);
        Title = NormalizeTitle(title);
        Status = FormStatus.Draft;
        CaseNumberPrefix = DefaultCaseNumberPrefix;
        CreatedAt = Utc.Require(createdAt, nameof(createdAt));
    }

    public Guid Id { get; private set; }

    /// <summary>Identifiant de l'URL publique courte, par exemple <c>inscription</c> pour <c>/f/inscription</c>.</summary>
    public string Slug { get; private set; }

    public string Title { get; private set; }

    public FormStatus Status { get; private set; }

    public Guid? CurrentVersionId { get; private set; }

    public DateTimeOffset? OpensAt { get; private set; }

    public DateTimeOffset? ClosesAt { get; private set; }

    /// <summary>Haché du code d'accès ; jamais le code en clair.</summary>
    public string? AccessCodeHash { get; private set; }

    public int? MaxResponses { get; private set; }

    public int? RetentionDays { get; private set; }

    /// <summary>Préfixe des numéros de dossier, par exemple <c>INS</c> pour <c>INS-2026-0042</c>.</summary>
    public string CaseNumberPrefix { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyList<FormVersion> Versions => _versions;

    public FormVersion? CurrentVersion => _versions.SingleOrDefault(v => v.Id == CurrentVersionId);

    public void Rename(string title) => Title = NormalizeTitle(title);

    public void SetSchedule(DateTimeOffset? opensAt, DateTimeOffset? closesAt)
    {
        if (opensAt is { } o)
        {
            Utc.Require(o, nameof(opensAt));
        }

        if (closesAt is { } c)
        {
            Utc.Require(c, nameof(closesAt));
        }

        if (opensAt is not null && closesAt is not null && closesAt <= opensAt)
        {
            throw new DomainException("La date de fermeture doit être postérieure à la date d'ouverture.");
        }

        OpensAt = opensAt;
        ClosesAt = closesAt;
    }

    public void SetMaxResponses(int? maxResponses)
    {
        if (maxResponses is <= 0)
        {
            throw new DomainException("Le nombre maximal de réponses doit être positif.");
        }

        MaxResponses = maxResponses;
    }

    public void SetRetentionDays(int? retentionDays)
    {
        if (retentionDays is <= 0)
        {
            throw new DomainException("La durée de conservation doit être positive.");
        }

        RetentionDays = retentionDays;
    }

    public void SetCaseNumberPrefix(string prefix) => CaseNumberPrefix = CaseNumber.NormalizePrefix(prefix);

    /// <summary>Le hachage est fait par l'appelant (couche Infrastructure) ; null supprime le code.</summary>
    public void SetAccessCodeHash(string? accessCodeHash) =>
        AccessCodeHash = string.IsNullOrWhiteSpace(accessCodeHash) ? null : accessCodeHash;

    /// <summary>
    /// Publie une définition : crée toujours une nouvelle version, sans toucher aux précédentes.
    /// </summary>
    public FormVersion Publish(
        string schemaJson,
        string? uiJson,
        string? pdfTemplateHtml,
        DateTimeOffset publishedAt,
        string? publishedBy)
    {
        var number = _versions.Count == 0 ? 1 : _versions.Max(v => v.Number) + 1;
        var version = new FormVersion(Id, number, schemaJson, uiJson, pdfTemplateHtml, publishedAt, publishedBy);

        _versions.Add(version);
        CurrentVersionId = version.Id;
        Status = FormStatus.Published;

        return version;
    }

    public void Close()
    {
        if (Status != FormStatus.Published)
        {
            throw new DomainException("Seul un formulaire publié peut être fermé.");
        }

        Status = FormStatus.Closed;
    }

    public void Reopen()
    {
        if (Status != FormStatus.Closed)
        {
            throw new DomainException("Seul un formulaire fermé peut être rouvert.");
        }

        Status = FormStatus.Published;
    }

    public bool IsAcceptingResponses(DateTimeOffset now, int responseCount) =>
        Status == FormStatus.Published
        && CurrentVersionId is not null
        && (OpensAt is null || now >= OpensAt)
        && (ClosesAt is null || now < ClosesAt)
        && (MaxResponses is null || responseCount < MaxResponses);

    /// <summary>
    /// Crée une réponse rattachée à la version publiée courante. Les données doivent
    /// déjà avoir été validées et normalisées par le moteur de formulaire.
    /// </summary>
    public Submission CreateSubmission(string caseNumber, string dataJson, DateTimeOffset now, int responseCount) =>
        CreateSubmission(CurrentVersionId ?? Guid.Empty, caseNumber, dataJson, now, responseCount);

    /// <summary>
    /// Variante qui exige que la version remplie par le répondant soit toujours la version courante :
    /// une réponse n'est jamais rattachée à une version qu'il n'a pas vue.
    /// </summary>
    public Submission CreateSubmission(Guid filledVersionId, string caseNumber, string dataJson, DateTimeOffset now, int responseCount)
    {
        Utc.Require(now, nameof(now));

        if (!IsAcceptingResponses(now, responseCount))
        {
            throw new DomainException("Le formulaire n'accepte pas de réponse.");
        }

        if (filledVersionId != CurrentVersionId)
        {
            throw new DomainException("La version remplie n'est plus la version publiée.");
        }

        var purgeAt = RetentionDays is { } days ? now.AddDays(days) : (DateTimeOffset?)null;

        return new Submission(CurrentVersionId!.Value, caseNumber, dataJson, now, purgeAt);
    }

    private static string NormalizeSlug(string slug)
    {
        var value = (slug ?? string.Empty).Trim().ToLowerInvariant();

        if (value.Length > SlugMaxLength || !SlugPattern().IsMatch(value))
        {
            throw new DomainException("Le slug ne doit contenir que des lettres minuscules, des chiffres et des tirets.");
        }

        return value;
    }

    private static string NormalizeTitle(string title)
    {
        var value = (title ?? string.Empty).Trim();

        if (value.Length == 0 || value.Length > TitleMaxLength)
        {
            throw new DomainException($"Le titre est obligatoire et limité à {TitleMaxLength} caractères.");
        }

        return value;
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();
}
