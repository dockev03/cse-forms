using CseForms.Application.Notifications;
using CseForms.Application.Settings;
using CseForms.Domain.Jobs;
using CseForms.Domain.Notifications;
using CseForms.Domain.Settings;
using CseForms.Infrastructure.Notifications;
using CseForms.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MimeKit;

namespace CseForms.Infrastructure.Tests;

public sealed class MailKitEmailSenderTests : IAsyncDisposable
{
    private readonly FakeSmtpServer _smtp = new();

    public ValueTask DisposeAsync() => _smtp.DisposeAsync();

    private Dictionary<string, string> Settings() => new()
    {
        [SettingKeys.SmtpHost] = "127.0.0.1",
        [SettingKeys.SmtpPort] = _smtp.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
        [SettingKeys.SmtpSecurity] = "none",
        [SettingKeys.SmtpFrom] = "formulaires@example.test",
        [SettingKeys.CseName] = "CSE Démo",
    };

    private static MailKitEmailSender Sender(IReadOnlyDictionary<string, string> settings, SmtpOptions? options = null) =>
        new(new StaticSettings(settings), Options.Create(options ?? new SmtpOptions()));

    [Fact]
    public async Task Message_is_sent_as_plain_text_with_the_settings_sender()
    {
        var message = new EmailMessage(["elu@example.test", "autre@example.test"], "Nouvelle réponse n° INS-2026-0042", "Dossier : INS-2026-0042\nRéponse accentuée.", "cse@example.test");

        await Sender(Settings()).SendAsync(message, default);

        var received = Assert.Single(_smtp.Received);
        Assert.Equal("formulaires@example.test", received.From);
        Assert.Equal(["elu@example.test", "autre@example.test"], received.To);

        using var raw = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(received.Raw));
        var mime = await MimeMessage.LoadAsync(raw);
        Assert.Equal("Nouvelle réponse n° INS-2026-0042", mime.Subject);
        Assert.Equal("CSE Démo", ((MailboxAddress)mime.From.Single()).Name);
        Assert.Equal("cse@example.test", ((MailboxAddress)mime.ReplyTo.Single()).Address);
        Assert.Null(mime.HtmlBody);
        Assert.Contains("Réponse accentuée.", mime.TextBody);
    }

    [Theory]
    [InlineData(SettingKeys.SmtpHost)]
    [InlineData(SettingKeys.SmtpFrom)]
    public async Task Missing_settings_are_reported(string missing)
    {
        var settings = Settings();
        settings.Remove(missing);

        await Assert.ThrowsAsync<EmailNotConfiguredException>(() =>
            Sender(settings).SendAsync(new EmailMessage(["elu@example.test"], "Objet", "Corps"), default));
        Assert.Empty(_smtp.Received);
    }

    [Fact]
    public async Task Credentials_are_never_sent_without_encryption()
    {
        var options = new SmtpOptions { Username = "compte", Password = "mot-de-passe-fictif" };

        await Assert.ThrowsAsync<EmailNotConfiguredException>(() =>
            Sender(Settings(), options).SendAsync(new EmailMessage(["elu@example.test"], "Objet", "Corps"), default));
        Assert.Empty(_smtp.Received);
    }

    [Fact]
    public async Task Server_refusal_is_raised_for_a_retry()
    {
        _smtp.RejectRecipients = true;

        await Assert.ThrowsAnyAsync<Exception>(() =>
            Sender(Settings()).SendAsync(new EmailMessage(["elu@example.test"], "Objet", "Corps"), default));
    }

    private sealed class StaticSettings(IReadOnlyDictionary<string, string> values) : ISettingReader
    {
        public Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken) => Task.FromResult(values);
    }
}

public class NotificationStoreTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Rule_is_stored_with_its_recipients_and_one_per_form()
    {
        Guid formId;
        await using (var db = fixture.CreateContext())
        {
            var form = await TestData.AddPublishedFormAsync(db, "notif-regle");
            formId = form.Id;
            db.NotificationRules.Add(new NotificationRule(form.Id, ["elu@example.test", "autre@example.test"], "Dossier {numero}", null));
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateContext())
        {
            var rule = await db.NotificationRules.SingleAsync(r => r.FormId == formId);
            Assert.Equal(["elu@example.test", "autre@example.test"], rule.Recipients);
            Assert.Equal("Dossier {numero}", rule.Subject);

            db.NotificationRules.Add(new NotificationRule(formId, ["x@example.test"], null, null));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task Email_source_carries_the_current_rule_and_settings()
    {
        Guid submissionId;
        await using (var db = fixture.CreateContext())
        {
            var form = await TestData.AddPublishedFormAsync(db, "notif-source");
            var submission = form.CreateSubmission($"TST-2026-{Random.Shared.Next(1000, 9999)}", """{"nom":"FICTIF"}""", Now, 0);
            db.Submissions.Add(submission);
            db.NotificationRules.Add(new NotificationRule(form.Id, ["elu@example.test"], null, "Corps {numero}"));
            if (await db.Settings.FindAsync(SettingKeys.SmtpFrom) is null)
            {
                db.Settings.Add(new Setting(SettingKeys.SmtpFrom, "formulaires@example.test"));
            }

            db.Jobs.Add(new SubmissionJob(submission.Id, JobType.Notification, Now));
            await db.SaveChangesAsync();
            submissionId = submission.Id;
        }

        await using var context = fixture.CreateContext();
        var store = new SubmissionEmailStore(context);
        var source = await store.GetSourceAsync(submissionId, default);

        Assert.NotNull(source);
        Assert.Equal("Formulaire fictif", source.FormTitle);
        Assert.Equal(new[] { "elu@example.test" }, source.Rule!.Recipients);
        Assert.Equal("Corps {numero}", source.Rule.Body);
        Assert.Equal("formulaires@example.test", source.Settings[SettingKeys.SmtpFrom]);
        Assert.Null(await store.GetSourceAsync(Guid.NewGuid(), default));

        var job = await context.Jobs.SingleAsync(j => j.SubmissionId == submissionId);
        Assert.Same(job, await store.FindJobAsync(job.Id, default));
    }
}
