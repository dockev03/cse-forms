using CseForms.Application.Settings;
using CseForms.Domain.Settings;
using CseForms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CseForms.Api.Cli;

/// <summary>
/// Paramètres de l'application (coordonnées et bureau du CSE, adresse publique) :
/// <c>settings set|unset|list</c>. Seules les clés connues sont acceptées.
/// </summary>
internal static class SettingCommands
{
    public const int ValueMaxLength = 4000;

    public static readonly string Usage = $"""
        Utilisation :
          settings set   --key <clé> --value <valeur>
          settings set   --key <clé> < fichier.txt     valeur sur plusieurs lignes, lue sur l'entrée standard
          settings unset --key <clé>
          settings list
        Clés :
        {string.Join(Environment.NewLine, SettingKeys.Descriptions.Select(d => $"  {d.Key,-18} {d.Value}"))}
        """;

    public static async Task<int> RunAsync(IServiceProvider services, string[] args, TextReader input, TextWriter output)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CseFormsDbContext>();
        var options = CliSupport.ParseOptions(args.Skip(2));

        try
        {
            return args.ElementAtOrDefault(1) switch
            {
                "set" => await SetAsync(db, options, input, output),
                "unset" => await UnsetAsync(db, KnownKey(options), output),
                "list" => await ListAsync(db, output),
                _ => CliSupport.Fail(output, Usage),
            };
        }
        catch (KeyNotFoundException ex)
        {
            return CliSupport.Fail(output, ex.Message);
        }
    }

    private static async Task<int> SetAsync(CseFormsDbContext db, Dictionary<string, string> options, TextReader input, TextWriter output)
    {
        var key = KnownKey(options);
        var value = Normalize(options.TryGetValue("value", out var given) ? given : await input.ReadToEndAsync());

        if (value.Length == 0 || value.Length > ValueMaxLength)
        {
            return CliSupport.Fail(output, $"Valeur vide ou trop longue ({ValueMaxLength} caractères au plus).");
        }

        if (key == SettingKeys.PublicBaseUrl)
        {
            value = value.TrimEnd('/');
        }

        if (SettingKeys.Validate(key, value) is { } error)
        {
            return CliSupport.Fail(output, error);
        }

        var setting = await db.Settings.FindAsync(key);
        if (setting is null)
        {
            db.Settings.Add(new Setting(key, value));
        }
        else
        {
            setting.Update(value);
        }

        await db.SaveChangesAsync();
        await output.WriteLineAsync($"Paramètre « {key} » enregistré.");
        return 0;
    }

    private static async Task<int> UnsetAsync(CseFormsDbContext db, string key, TextWriter output)
    {
        var setting = await db.Settings.FindAsync(key);
        if (setting is not null)
        {
            db.Settings.Remove(setting);
            await db.SaveChangesAsync();
        }

        await output.WriteLineAsync($"Paramètre « {key} » supprimé.");
        return 0;
    }

    private static async Task<int> ListAsync(CseFormsDbContext db, TextWriter output)
    {
        var values = await db.Settings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value);
        foreach (var key in SettingKeys.Descriptions.Keys)
        {
            var value = values.TryGetValue(key, out var v) ? v.Replace("\n", " / ", StringComparison.Ordinal) : "(non défini)";
            await output.WriteLineAsync($"{key} | {value}");
        }

        return 0;
    }

    private static string KnownKey(Dictionary<string, string> options)
    {
        var key = CliSupport.Required(options, Usage, "key").Trim();
        return SettingKeys.IsKnown(key) ? key : throw new KeyNotFoundException($"Clé inconnue.{Environment.NewLine}{Usage}");
    }

    /// <summary>Retours à la ligne unifiés, espaces de fin de ligne et lignes vides de début et de fin supprimés.</summary>
    private static string Normalize(string value) =>
        string.Join('\n', value.ReplaceLineEndings("\n").Split('\n').Select(line => line.TrimEnd())).Trim();
}
