using System.Text.Json.Nodes;
using CseForms.Application.FormAdministration;
using CseForms.Application.Jobs;
using CseForms.Application.Notifications;
using CseForms.Application.Pdf;
using CseForms.Application.Settings;
using CseForms.Domain.Jobs;
using Microsoft.Extensions.Logging.Abstractions;

namespace CseForms.Application.Tests;

/// <summary>Données fictives d'une inscription : aucune ne doit apparaître dans un email.</summary>
internal static class FictiveInscription
{
    // 14:05 UTC le 4 novembre 2026, soit 15:05 à Paris.
    public static readonly DateTimeOffset Submitted = new(2026, 11, 4, 14, 5, 0, TimeSpan.Zero);

    public const string DataJson = """
        {"civilite":"Mme","nom":"FICTIF","prenom":"Camille","contrat":"CDD","dateEmbauche":"2026-09-01","dateFin":"2027-08-31",
         "site":"Riom","adresse":{"street":"1 rue de l'Exemple","postalCode":"63200","city":"MOZAC"},"emailPerso":"camille.fictif@example.test",
         "conjointNom":"EXEMPLE","conjointPrenom":"Dominique",
         "enfants":[{"nom":"FICTIF","prenom":"Léa","naissance":"2020-05-01","sexe":"F"}],"rgpdLu":true,"attestation":true}
        """;

    /// <summary>Valeurs saisies qui ne doivent jamais sortir dans un email (l'adresse du destinataire mise à part).</summary>
    public static readonly string[] PersonalValues =
        ["FICTIF", "Camille", "CDD", "Exemple", "63200", "MOZAC", "EXEMPLE", "Dominique", "Léa", "2020", "01/09/2026", "31/08/2027", "camille.fictif"];

    public static readonly IReadOnlyDictionary<string, string> Settings = new Dictionary<string, string>
    {
        [SettingKeys.CseName] = "CSE Démo",
        [SettingKeys.CseEmail] = "cse@example.test",
        [SettingKeys.CsePhone] = "04 00 00 00 00",
        [SettingKeys.PublicBaseUrl] = "https://formulaires.example.test",
        [SettingKeys.AlertsEmail] = "admin@example.test",
    };

    public static string SchemaJson =>
        FormPackageReader.Read(FormPackageReaderTests.InscriptionJson(), FormPackageReaderTests.ReadFormsFile).Package!.SchemaJson;

    public static EmailSource Source(NotificationRuleView? rule = null, string? dataJson = null, string? schemaJson = null, IReadOnlyDictionary<string, string>? settings = null) =>
        new(Guid.Parse("0190f0a0-0000-7000-8000-000000000042"), "Fiche d'inscription au CSE", "INS-2026-0042", Submitted,
            schemaJson ?? SchemaJson, dataJson ?? DataJson, rule, settings ?? Settings);

    public static void AssertNoPersonalData(EmailMessage message)
    {
        foreach (var value in PersonalValues)
        {
            Assert.DoesNotContain(value, message.Subject, StringComparison.OrdinalIgnoreCase);
            Assert.False(message.Body.Contains(value, StringComparison.OrdinalIgnoreCase), $"« {value} » trouvé dans le corps.");
        }
    }
}

public class NotificationMessageTests
{
    private static readonly NotificationRuleView DefaultRule = new(["elu@example.test", "autre@example.test"], null, null);

    [Fact]
    public void Default_notification_gives_case_form_date_site_and_link_only()
    {
        var message = SubmissionEmailService.BuildNotification(FictiveInscription.Source(DefaultRule))!;

        Assert.Equal(["elu@example.test", "autre@example.test"], message.To);
        Assert.Equal("Nouvelle réponse n° INS-2026-0042 : Fiche d'inscription au CSE", message.Subject);
        Assert.Contains("Dossier : INS-2026-0042", message.Body);
        Assert.Contains("Date : 04/11/2026", message.Body);
        Assert.Contains("Site : Riom", message.Body);
        Assert.Contains("https://formulaires.example.test/admin/reponses/0190f0a0-0000-7000-8000-000000000042", message.Body);
        Assert.Null(message.ReplyTo);
        FictiveInscription.AssertNoPersonalData(message);
    }

    [Fact]
    public void Custom_texts_use_variables_and_always_carry_the_link()
    {
        var rule = new NotificationRuleView(["elu@example.test"], "Nouvelle inscription n° {numero}, site {site}", "Inscription du {date} ({formulaire}).");

        var message = SubmissionEmailService.BuildNotification(FictiveInscription.Source(rule))!;

        Assert.Equal("Nouvelle inscription n° INS-2026-0042, site Riom", message.Subject);
        Assert.StartsWith("Inscription du 04/11/2026 (Fiche d'inscription au CSE).", message.Body);
        Assert.EndsWith("/admin/reponses/0190f0a0-0000-7000-8000-000000000042", message.Body);
        FictiveInscription.AssertNoPersonalData(message);
    }

    [Fact]
    public void Site_line_is_left_out_when_no_site_was_given()
    {
        var message = SubmissionEmailService.BuildNotification(FictiveInscription.Source(DefaultRule, dataJson: """{"nom":"FICTIF"}"""))!;

        Assert.DoesNotContain("Site :", message.Body);
    }

    [Fact]
    public void Form_without_rule_sends_no_notification() =>
        Assert.Null(SubmissionEmailService.BuildNotification(FictiveInscription.Source()));

    [Fact]
    public void Link_needs_the_public_address()
    {
        var settings = FictiveInscription.Settings.Where(s => s.Key != SettingKeys.PublicBaseUrl).ToDictionary();

        Assert.Throws<EmailNotConfiguredException>(() => SubmissionEmailService.BuildNotification(FictiveInscription.Source(DefaultRule, settings: settings)));
    }

    [Fact]
    public void Receipt_goes_to_the_respondent_without_any_entered_data()
    {
        var message = SubmissionEmailService.BuildReceipt(FictiveInscription.Source())!;

        Assert.Equal(["camille.fictif@example.test"], message.To);
        Assert.Equal("Accusé de réception : dossier n° INS-2026-0042", message.Subject);
        Assert.Contains("CSE Démo a bien reçu votre formulaire « Fiche d'inscription au CSE » le 04/11/2026 à 15:05.", message.Body);
        Assert.Contains("Numéro de dossier : INS-2026-0042", message.Body);
        Assert.Contains("Toute modification d'état civil doit être communiquée au CSE.", message.Body);
        Assert.Contains("Contact : cse@example.test ; 04 00 00 00 00", message.Body);
        Assert.Equal("cse@example.test", message.ReplyTo);
        FictiveInscription.AssertNoPersonalData(message);
    }

    [Theory]
    [InlineData("""{"nom":"FICTIF"}""")]
    [InlineData("""{"emailPerso":"pas une adresse"}""")]
    [InlineData("""{"emailPerso":"a@example.test\r\nBcc: b@example.test"}""")]
    public void Receipt_needs_a_valid_address(string dataJson) =>
        Assert.Null(SubmissionEmailService.BuildReceipt(FictiveInscription.Source(dataJson: dataJson)));

    [Fact]
    public void Form_without_receipt_sends_none()
    {
        var schema = """{"title":"T","sections":[{"id":"s","fields":[{"id":"mail","type":"email","label":"Email"}]}]}""";

        Assert.Null(SubmissionEmailService.BuildReceipt(FictiveInscription.Source(dataJson: """{"mail":"a@example.test"}""", schemaJson: schema)));
    }

    [Fact]
    public void Subject_never_spans_several_lines()
    {
        var rule = new NotificationRuleView(["elu@example.test"], "{formulaire}", null);
        var source = FictiveInscription.Source(rule) with { FormTitle = "Titre\r\nBcc: x@example.test" };

        Assert.DoesNotContain('\n', SubmissionEmailService.BuildNotification(source)!.Subject);
    }

    [Theory]
    [InlineData("Dossier {numero} du {date}", new string[0])]
    [InlineData("Bonjour {prénom}, {nom} {numero}", new[] { "prénom", "nom" })]
    public void Unknown_variables_are_reported(string text, string[] expected) =>
        Assert.Equal(expected, NotificationTemplate.UnknownVariables(text));
}

public class TestEmailTests
{
    [Fact]
    public void Test_email_shows_the_settings_without_any_secret()
    {
        var settings = new Dictionary<string, string>
        {
            [SettingKeys.SmtpHost] = "smtp.example.test",
            [SettingKeys.SmtpSecurity] = "ssl",
            [SettingKeys.SmtpPort] = "465",
            [SettingKeys.SmtpFrom] = "formulaires@example.test",
        };

        var message = TestEmail.Build("admin@example.test", settings, FictiveInscription.Submitted);

        Assert.Equal(["admin@example.test"], message.To);
        Assert.Equal("[Formulaires CSE] Email de test", message.Subject);
        Assert.Contains("Serveur : smtp.example.test, port 465, chiffrement ssl", message.Body);
        Assert.Contains("Expéditeur : formulaires@example.test", message.Body);
        Assert.Contains("Envoyé le : 04/11/2026 à 15:05", message.Body);
        Assert.DoesNotContain("mot de passe", message.Body, StringComparison.OrdinalIgnoreCase);
    }
}

public class SubmissionEmailServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeEmailStore _store = new();
    private readonly FakeEmailSender _sender = new();
    private readonly RecordingAlerter _alerter = new();

    private SubmissionEmailService Service() =>
        new(_store, _sender, _alerter, new FixedTime(Now), NullLogger<SubmissionEmailService>.Instance);

    [Theory]
    [InlineData(JobType.Notification, "elu@example.test")]
    [InlineData(JobType.Receipt, "camille.fictif@example.test")]
    public async Task Message_is_sent_once_and_the_job_closed(JobType type, string recipient)
    {
        _store.Use(type, FictiveInscription.Source(new NotificationRuleView(["elu@example.test"], null, null)));

        Assert.Equal(JobRunResult.Done, await Service().RunJobAsync(_store.Job!.Id, default));
        Assert.Equal(JobRunResult.Done, await Service().RunJobAsync(_store.Job.Id, default));

        var sent = Assert.Single(_sender.Sent);
        Assert.Equal([recipient], sent.To);
        Assert.Equal(JobStatus.Succeeded, _store.Job.Status);
    }

    [Fact]
    public async Task Nothing_to_send_closes_the_job_without_sending()
    {
        _store.Use(JobType.Notification, FictiveInscription.Source());

        Assert.Equal(JobRunResult.Done, await Service().RunJobAsync(_store.Job!.Id, default));

        Assert.Empty(_sender.Sent);
        Assert.Equal(JobStatus.Succeeded, _store.Job.Status);
    }

    [Fact]
    public async Task Sending_failure_is_retried_then_alerted_once()
    {
        _store.Use(JobType.Receipt, FictiveInscription.Source());
        _sender.Fail = true;

        Assert.Equal(JobRunResult.Retry, await Service().RunJobAsync(_store.Job!.Id, default));
        Assert.Equal(JobRunResult.Retry, await Service().RunJobAsync(_store.Job.Id, default));
        Assert.Empty(_alerter.Failures);
        Assert.Equal(JobRunResult.Done, await Service().RunJobAsync(_store.Job.Id, default));

        Assert.Equal(JobStatus.Failed, _store.Job.Status);
        Assert.Equal(nameof(IOException), _store.Job.LastError);
        var alert = Assert.Single(_alerter.Failures);
        Assert.Equal((JobType.Receipt, "INS-2026-0042"), (alert.Type, alert.CaseNumber));
    }

    [Fact]
    public async Task Deleted_submission_fails_without_sending()
    {
        _store.Use(JobType.Notification, null);

        for (var i = 0; i < SubmissionJob.MaxAttempts; i++)
        {
            await Service().RunJobAsync(_store.Job!.Id, default);
        }

        Assert.Empty(_sender.Sent);
        Assert.Equal(JobStatus.Failed, _store.Job!.Status);
        Assert.Null(Assert.Single(_alerter.Failures).CaseNumber);
    }

    private sealed class FakeEmailStore : ISubmissionEmailStore
    {
        private EmailSource? _source;

        public SubmissionJob? Job { get; private set; }

        public void Use(JobType type, EmailSource? source)
        {
            _source = source;
            Job = new SubmissionJob(source?.SubmissionId ?? Guid.NewGuid(), type, Now);
        }

        public Task<SubmissionJob?> FindJobAsync(Guid jobId, CancellationToken cancellationToken) =>
            Task.FromResult(Job?.Id == jobId ? Job : null);

        public Task<EmailSource?> GetSourceAsync(Guid submissionId, CancellationToken cancellationToken) => Task.FromResult(_source);

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

public class JobFailureAlerterTests
{
    private static readonly JobFailure Failure =
        new(Guid.NewGuid(), JobType.Notification, Guid.Parse("0190f0a0-0000-7000-8000-000000000042"), "INS-2026-0042", "SmtpCommandException");

    private readonly FakeEmailSender _sender = new();

    private EmailJobFailureAlerter Alerter(IReadOnlyDictionary<string, string> settings) =>
        new(new FakeSettings(settings), _sender, NullLogger<EmailJobFailureAlerter>.Instance);

    [Fact]
    public async Task Alert_goes_to_the_alert_address_with_identifiers_only()
    {
        await Alerter(FictiveInscription.Settings).AlertAsync(Failure, default);

        var message = Assert.Single(_sender.Sent);
        Assert.Equal(["admin@example.test"], message.To);
        Assert.Equal("[Formulaires CSE] Échec définitif : notification au CSE", message.Subject);
        Assert.Contains("Dossier : INS-2026-0042", message.Body);
        Assert.Contains("Cause technique : SmtpCommandException", message.Body);
        Assert.Contains("https://formulaires.example.test/admin/reponses/0190f0a0-0000-7000-8000-000000000042", message.Body);
    }

    [Fact]
    public async Task No_alert_address_means_no_alert()
    {
        await Alerter(new Dictionary<string, string>()).AlertAsync(Failure, default);

        Assert.Empty(_sender.Sent);
    }

    [Fact]
    public async Task Failing_alert_never_throws()
    {
        _sender.Fail = true;

        await Alerter(FictiveInscription.Settings).AlertAsync(Failure, default);
    }

    private sealed class FakeSettings(IReadOnlyDictionary<string, string> values) : ISettingReader
    {
        public Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken) => Task.FromResult(values);
    }
}

public class ReceiptSettingsTests
{
    private const string Sections = """
        [{"id":"s","fields":[
          {"id":"mail","type":"email","label":"Email"},
          {"id":"confirmation","type":"email","label":"Confirmation","persist":false},
          {"id":"nom","type":"text","label":"Nom"}]}]
        """;

    private static FormPackageResult Read(string receipt) =>
        FormPackageReader.Read($$"""{"slug":"essai","title":"T","access":{"mode":"public"},"receipt":{{receipt}},"sections":{{Sections}}}""");

    [Fact]
    public void Receipt_with_email_field_and_message_is_accepted()
    {
        var result = Read("""{"emailField":"mail","message":"Merci."}""");

        Assert.True(result.IsValid);
        Assert.Equal(new ReceiptSettings("mail", "Merci."), ReceiptSettings.Read(result.Package!.SchemaJson));
    }

    [Theory]
    [InlineData("""{"emailField":"nom"}""", "$.receipt.emailField")]
    [InlineData("""{"emailField":"confirmation"}""", "$.receipt.emailField")]
    [InlineData("""{"emailField":"absent"}""", "$.receipt.emailField")]
    [InlineData("""{"message":"Merci."}""", "$.receipt.emailField")]
    [InlineData("""{"emailField":"mail","includeData":true}""", "$.receipt.includeData")]
    [InlineData("""{"emailField":"mail","message":""}""", "$.receipt.message")]
    [InlineData("""["mail"]""", "$.receipt")]
    public void Invalid_receipt_is_refused(string receipt, string path) =>
        Assert.Contains(Read(receipt).Errors, e => e.Path == path);
}

public sealed class RecordingAlerter : IJobFailureAlerter
{
    public List<JobFailure> Failures { get; } = [];

    public Task AlertAsync(JobFailure failure, CancellationToken cancellationToken)
    {
        Failures.Add(failure);
        return Task.CompletedTask;
    }
}

public sealed class FakeEmailSender : IEmailSender
{
    public List<EmailMessage> Sent { get; } = [];

    public bool Fail { get; set; }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        if (Fail)
        {
            throw new IOException("Serveur SMTP indisponible.");
        }

        Sent.Add(message);
        return Task.CompletedTask;
    }
}
