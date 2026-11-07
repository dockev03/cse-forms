using CseForms.Application.Notifications;
using CseForms.Domain.Common;
using CseForms.Domain.Notifications;
using CseForms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CseForms.Api.Cli;

/// <summary>
/// Règle de notification d'un formulaire : <c>notifications set|show|remove</c>. Les destinataires sont
/// réglés en base, jamais dans le fichier de formulaire versionné.
/// </summary>
internal static class NotificationCommands
{
    public static readonly string Usage = $"""
        Utilisation :
          notifications set    --form <slug> --to <adresse>[,<adresse>...] [--subject <objet>] [--body-file <fichier.txt>]
          notifications show   --form <slug>
          notifications remove --form <slug>
        Variables de l'objet et du corps : {string.Join(", ", NotificationTemplate.Variables.Select(v => $"{{{v}}}"))}.
        Sans objet ni corps, un message par défaut est envoyé. Le lien vers la réponse est toujours ajouté.
        """;

    public static async Task<int> RunAsync(IServiceProvider services, string[] args, TextWriter output)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CseFormsDbContext>();
        var options = CliSupport.ParseOptions(args.Skip(2));

        try
        {
            return args.ElementAtOrDefault(1) switch
            {
                "set" => await SetAsync(db, options, output),
                "show" => await ShowAsync(db, options, output),
                "remove" => await RemoveAsync(db, options, output),
                _ => CliSupport.Fail(output, Usage),
            };
        }
        catch (KeyNotFoundException ex)
        {
            return CliSupport.Fail(output, ex.Message);
        }
    }

    private static async Task<int> SetAsync(CseFormsDbContext db, Dictionary<string, string> options, TextWriter output)
    {
        var formId = await FormIdAsync(db, options);
        if (formId is null)
        {
            return CliSupport.Fail(output, "Formulaire introuvable.");
        }

        var recipients = CliSupport.Required(options, Usage, "to").Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var subject = options.GetValueOrDefault("subject");
        string? body = null;
        if (options.TryGetValue("body-file", out var bodyFile))
        {
            if (!File.Exists(bodyFile) || new FileInfo(bodyFile).Length > NotificationRule.BodyMaxLength * 4)
            {
                return CliSupport.Fail(output, "Fichier du corps introuvable ou trop volumineux.");
            }

            body = await File.ReadAllTextAsync(bodyFile);
        }

        var unknown = NotificationTemplate.UnknownVariables(subject).Concat(NotificationTemplate.UnknownVariables(body)).Distinct().ToList();
        if (unknown.Count > 0)
        {
            return CliSupport.Fail(output, $"Variable(s) inconnue(s) : {string.Join(", ", unknown.Select(v => $"{{{v}}}"))}. "
                + $"Variables possibles : {string.Join(", ", NotificationTemplate.Variables.Select(v => $"{{{v}}}"))}.");
        }

        var rule = await db.NotificationRules.SingleOrDefaultAsync(r => r.FormId == formId);
        try
        {
            if (rule is null)
            {
                db.NotificationRules.Add(new NotificationRule(formId.Value, recipients, subject, body));
            }
            else
            {
                rule.Update(recipients, subject, body);
            }
        }
        catch (DomainException ex)
        {
            return CliSupport.Fail(output, ex.Message);
        }

        await db.SaveChangesAsync();
        await output.WriteLineAsync("Règle de notification enregistrée.");
        return 0;
    }

    private static async Task<int> ShowAsync(CseFormsDbContext db, Dictionary<string, string> options, TextWriter output)
    {
        var formId = await FormIdAsync(db, options);
        if (formId is null)
        {
            return CliSupport.Fail(output, "Formulaire introuvable.");
        }

        var rule = await db.NotificationRules.AsNoTracking().SingleOrDefaultAsync(r => r.FormId == formId);
        if (rule is null)
        {
            await output.WriteLineAsync("Aucune notification pour ce formulaire.");
            return 0;
        }

        await output.WriteLineAsync($"Destinataires : {string.Join(", ", rule.Recipients)}");
        await output.WriteLineAsync($"Objet : {rule.Subject ?? $"(par défaut) {NotificationTemplate.DefaultSubject}"}");
        await output.WriteLineAsync(rule.Body is null ? "Corps : (par défaut)" : $"Corps :{Environment.NewLine}{rule.Body}");
        return 0;
    }

    private static async Task<int> RemoveAsync(CseFormsDbContext db, Dictionary<string, string> options, TextWriter output)
    {
        var formId = await FormIdAsync(db, options);
        if (formId is null)
        {
            return CliSupport.Fail(output, "Formulaire introuvable.");
        }

        await db.NotificationRules.Where(r => r.FormId == formId).ExecuteDeleteAsync();
        await output.WriteLineAsync("Notifications désactivées pour ce formulaire.");
        return 0;
    }

    private static async Task<Guid?> FormIdAsync(CseFormsDbContext db, Dictionary<string, string> options)
    {
        var slug = CliSupport.Required(options, Usage, "form").Trim().ToLowerInvariant();
        var form = await db.Forms.AsNoTracking().Where(f => f.Slug == slug).Select(f => new { f.Id }).SingleOrDefaultAsync();
        return form?.Id;
    }
}
