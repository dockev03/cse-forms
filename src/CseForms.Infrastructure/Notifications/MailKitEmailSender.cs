using System.Globalization;
using CseForms.Application.Notifications;
using CseForms.Application.Settings;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace CseForms.Infrastructure.Notifications;

/// <summary>Identifiants SMTP : uniquement en variables d'environnement (<c>Smtp__Username</c>, <c>Smtp__Password</c>).</summary>
public sealed class SmtpOptions
{
    public const string Section = "Smtp";

    public string? Username { get; set; }

    public string? Password { get; set; }

    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// Envoi SMTP direct avec MailKit. Serveur, port, chiffrement et expéditeur viennent des paramètres en base,
/// lus à chaque envoi ; les identifiants viennent de l'environnement. Jamais d'authentification sans chiffrement.
/// </summary>
internal sealed class MailKitEmailSender(ISettingReader settings, IOptions<SmtpOptions> options) : IEmailSender
{
    public const int DefaultPort = 587;

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var values = await settings.GetAllAsync(cancellationToken);
        string? Setting(string key) => values.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

        var host = Setting(SettingKeys.SmtpHost) ?? throw new EmailNotConfiguredException("Serveur SMTP absent (smtp.host).");
        var from = Setting(SettingKeys.SmtpFrom) ?? throw new EmailNotConfiguredException("Adresse d'expédition absente (smtp.from).");
        var port = Setting(SettingKeys.SmtpPort) is { } p ? int.Parse(p, CultureInfo.InvariantCulture) : DefaultPort;
        var security = (Setting(SettingKeys.SmtpSecurity) ?? "starttls") switch
        {
            "ssl" => SecureSocketOptions.SslOnConnect,
            "none" => SecureSocketOptions.None,
            _ => SecureSocketOptions.StartTls,
        };

        var smtp = options.Value;
        var authenticate = !string.IsNullOrEmpty(smtp.Username);
        if (authenticate && security == SecureSocketOptions.None)
        {
            throw new EmailNotConfiguredException("Authentification SMTP refusée sans chiffrement (smtp.security = none).");
        }

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(Setting(SettingKeys.CseName) ?? "CSE", from));
        foreach (var to in message.To)
        {
            mime.To.Add(MailboxAddress.Parse(to));
        }

        if (message.ReplyTo is not null)
        {
            mime.ReplyTo.Add(MailboxAddress.Parse(message.ReplyTo));
        }

        mime.Subject = message.Subject;
        mime.Body = new TextPart("plain") { Text = message.Body };

        using var client = new SmtpClient { Timeout = Math.Clamp(smtp.TimeoutSeconds, 5, 300) * 1000 };
        await client.ConnectAsync(host, port, security, cancellationToken);
        if (authenticate)
        {
            await client.AuthenticateAsync(smtp.Username, smtp.Password ?? string.Empty, cancellationToken);
        }

        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }
}
