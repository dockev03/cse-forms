using CseForms.Application.Notifications;
using CseForms.Domain.Jobs;
using CseForms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CseForms.Infrastructure.Notifications;

internal sealed class SubmissionEmailStore(CseFormsDbContext db) : ISubmissionEmailStore
{
    public Task<SubmissionJob?> FindJobAsync(Guid jobId, CancellationToken cancellationToken) =>
        db.Jobs.SingleOrDefaultAsync(j => j.Id == jobId, cancellationToken);

    public async Task<EmailSource?> GetSourceAsync(Guid submissionId, CancellationToken cancellationToken)
    {
        var row = await (
            from s in db.Submissions.AsNoTracking()
            join v in db.FormVersions on s.FormVersionId equals v.Id
            join f in db.Forms on v.FormId equals f.Id
            where s.Id == submissionId
            select new { s.Id, FormId = f.Id, f.Title, s.CaseNumber, s.CreatedAt, v.SchemaJson, s.DataJson })
            .SingleOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        // La règle en vigueur au moment de l'envoi : un changement d'adresse s'applique sans republier.
        var rule = await db.NotificationRules.AsNoTracking().SingleOrDefaultAsync(r => r.FormId == row.FormId, cancellationToken);
        var settings = await db.Settings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);

        return new EmailSource(
            row.Id,
            row.Title,
            row.CaseNumber,
            row.CreatedAt,
            row.SchemaJson,
            row.DataJson,
            rule is null ? null : new NotificationRuleView(rule.Recipients, rule.Subject, rule.Body),
            settings);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}

internal sealed class SettingReader(CseFormsDbContext db) : ISettingReader
{
    public async Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken) =>
        await db.Settings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);
}
