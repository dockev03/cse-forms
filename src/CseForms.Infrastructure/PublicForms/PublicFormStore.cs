using CseForms.Application.PublicForms;
using CseForms.Domain.Common;
using CseForms.Domain.Forms;
using CseForms.Domain.Jobs;
using CseForms.Domain.Submissions;
using CseForms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CseForms.Infrastructure.PublicForms;

internal sealed class PublicFormStore(CseFormsDbContext db) : IPublicFormStore
{
    public async Task<PublishedForm?> FindPublishedAsync(string slug, CancellationToken cancellationToken)
    {
        var normalized = (slug ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length is 0 or > Form.SlugMaxLength)
        {
            return null;
        }

        var form = await db.Forms
            .AsNoTracking()
            .Include(f => f.Versions)
            .SingleOrDefaultAsync(f => f.Slug == normalized && f.Status != FormStatus.Draft, cancellationToken);

        if (form?.CurrentVersion is not { } version)
        {
            return null;
        }

        return new PublishedForm(form, version, await CountResponsesAsync(form.Id, cancellationToken));
    }

    public async Task<IReadOnlyCollection<string>> GetActiveSiteLabelsAsync(CancellationToken cancellationToken) =>
        await db.Sites.AsNoTracking().Where(s => s.IsActive).Select(s => s.Label).ToListAsync(cancellationToken);

    public async Task<SaveSubmissionOutcome> SaveSubmissionAsync(SaveSubmissionCommand command, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Verrou sur la ligne du formulaire : sérialise les soumissions d'un même formulaire
        // pour que le contrôle du nombre maximal de réponses reste exact.
        await db.Database.ExecuteSqlAsync($"SELECT 1 FROM form WHERE id = {command.FormId} FOR UPDATE", cancellationToken);

        var form = await db.Forms
            .Include(f => f.Versions)
            .SingleOrDefaultAsync(f => f.Id == command.FormId, cancellationToken);

        if (form is null)
        {
            return new SaveSubmissionOutcome.NotAccepting();
        }

        if (form.CurrentVersionId != command.FilledVersionId)
        {
            return new SaveSubmissionOutcome.VersionChanged();
        }

        var responseCount = await CountResponsesAsync(form.Id, cancellationToken);
        if (!form.IsAcceptingResponses(command.Now, responseCount))
        {
            return new SaveSubmissionOutcome.NotAccepting();
        }

        var sequence = await NextSequenceAsync(form.CaseNumberPrefix, command.CaseYear, cancellationToken);
        var caseNumber = CaseNumber.Format(form.CaseNumberPrefix, command.CaseYear, sequence);

        Submission submission;
        try
        {
            submission = form.CreateSubmission(command.FilledVersionId, caseNumber, command.DataJson, command.Now, responseCount);
        }
        catch (DomainException)
        {
            return new SaveSubmissionOutcome.NotAccepting();
        }

        db.Submissions.Add(submission);

        // Même transaction que la réponse : le PDF sera produit même si le serveur s'arrête ici.
        db.Jobs.Add(new SubmissionJob(submission.Id, JobType.Pdf, command.Now));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new SaveSubmissionOutcome.Created(submission.Id, caseNumber);
    }

    private Task<int> CountResponsesAsync(Guid formId, CancellationToken cancellationToken) =>
        db.Submissions.CountAsync(
            s => db.FormVersions.Any(v => v.Id == s.FormVersionId && v.FormId == formId),
            cancellationToken);

    /// <summary>Incrément atomique : jamais deux fois le même rang, même après purge des réponses.</summary>
    private async Task<long> NextSequenceAsync(string prefix, int year, CancellationToken cancellationToken)
    {
        // ToListAsync : une requête INSERT ... RETURNING ne peut pas être composée par EF.
        var values = await db.Database
            .SqlQuery<long>($"""
                INSERT INTO case_number_counter (prefix, year, last_value)
                VALUES ({prefix}, {year}, 1)
                ON CONFLICT (prefix, year) DO UPDATE SET last_value = case_number_counter.last_value + 1
                RETURNING last_value AS "Value"
                """)
            .ToListAsync(cancellationToken);

        return values.Single();
    }
}
