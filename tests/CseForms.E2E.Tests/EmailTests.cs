using CseForms.E2E.Tests.Infrastructure;
using CseForms.Testing;
using MimeKit;

namespace CseForms.E2E.Tests;

/// <summary>Emails de bout en bout : soumission, tâches du Worker, envoi SMTP réel vers un serveur factice.</summary>
[Collection(E2ECollection.Name)]
public sealed class EmailTests(E2EEnvironment app)
{
    [E2EFact]
    public async Task Submission_sends_a_notification_and_a_receipt_without_entered_data()
    {
        var caseNumber = await FictiveSubmissions.SubmitCdiAsync(app, nom: "Courriel");

        var notification = await WaitForAsync(E2EEnvironment.NotificationRecipient, caseNumber);
        var receipt = await WaitForAsync("camille.exemple@example.test", caseNumber);

        Assert.Equal($"Nouvelle inscription n° {caseNumber}, site Abrest", notification.Subject);
        Assert.Matches($@"{E2EEnvironment.PublicBaseUrl}/admin/reponses/[0-9a-f-]{{36}}", notification.TextBody);
        Assert.Equal($"Accusé de réception : dossier n° {caseNumber}", receipt.Subject);
        Assert.Contains("Toute modification d'état civil doit être communiquée au CSE.", receipt.TextBody);
        Assert.Equal("cse.e2e@example.test", ((MailboxAddress)receipt.ReplyTo.Single()).Address);

        // Aucune valeur saisie, ni dans l'objet ni dans le corps ; jamais de pièce jointe.
        foreach (var message in new[] { notification, receipt })
        {
            foreach (var value in new[] { "Courriel", "COURRIEL", "Camille", "Exemple", "63200", "04/03/2024", "Lou", "CDI" })
            {
                Assert.DoesNotContain(value, message.Subject, StringComparison.Ordinal);
                Assert.DoesNotContain(value, message.TextBody, StringComparison.Ordinal);
            }

            Assert.Empty(message.Attachments);
            Assert.Null(message.HtmlBody);
        }

        Assert.DoesNotContain("camille.exemple", notification.TextBody, StringComparison.Ordinal);
    }

    private async Task<MimeMessage> WaitForAsync(string recipient, string caseNumber)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            foreach (var email in app.Smtp.Received.Where(e => e.To.Contains(recipient)))
            {
                using var raw = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(email.Raw));
                var message = await MimeMessage.LoadAsync(raw);
                if (message.Subject.Contains(caseNumber, StringComparison.Ordinal))
                {
                    return message;
                }
            }

            await Task.Delay(500);
        }

        throw new TimeoutException($"Aucun email pour le dossier {caseNumber} après 60 secondes.{Environment.NewLine}{app.ServerOutput}");
    }

    [E2EFact]
    public async Task Mail_test_command_sends_a_test_email_with_the_worker_settings()
    {
        var output = await app.RunWorkerCommandAsync("mail", "test", "--to", "test.smtp@example.test");

        Assert.Contains("Email de test envoyé", output);
        var message = await WaitForSubjectAsync("test.smtp@example.test", "[Formulaires CSE] Email de test");
        Assert.Contains("Serveur : 127.0.0.1", message.TextBody);
        Assert.Contains("Expéditeur : formulaires.e2e@example.test", message.TextBody);

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => app.RunWorkerCommandAsync("mail", "test", "--to", "pas-une-adresse"));
        Assert.Contains("Adresse email invalide", refused.Message);
    }

    private async Task<MimeMessage> WaitForSubjectAsync(string recipient, string subject)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            foreach (var email in app.Smtp.Received.Where(e => e.To.Contains(recipient)))
            {
                using var raw = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(email.Raw));
                var message = await MimeMessage.LoadAsync(raw);
                if (message.Subject == subject)
                {
                    return message;
                }
            }

            await Task.Delay(200);
        }

        throw new TimeoutException($"Aucun email « {subject} » pour {recipient}.");
    }
}
