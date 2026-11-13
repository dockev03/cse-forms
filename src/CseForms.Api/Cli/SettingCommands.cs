using CseForms.Application.Settings;
using CseForms.Domain.Settings;
using CseForms.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CseForms.Api.Cli;

/// <summary>
/// Paramètres de l'application (coordonnées, bureau et logo du CSE, adresse publique, apparence) :
/// <c>settings set|logo|background|unset|list</c>. Seules les clés connues sont acceptées.
/// </summary>
internal static class SettingCommands
{
    public static readonly string Usage = $"""
        Utilisation :
          settings set   --key <clé> --value <valeur>
          settings set   --key <clé> < fichier.txt     valeur sur plusieurs lignes, lue sur l'entrée standard
          settings logo  --file <image>              logo du CSE : {CseLogo.Rule}
          settings background --file <image>         fond du formulaire public : {BackgroundImage.Rule}
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
                "logo" => await ImageAsync(db, SettingKeys.CseLogo, "Logo refusé", "Logo du CSE enregistré.", options, output),
                "background" => await ImageAsync(db, SettingKeys.ThemeBackground, "Image de fond refusée", "Image de fond enregistrée.", options, output),
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
        if (key == SettingKeys.CseLogo)
        {
            return CliSupport.Fail(output, "Le logo se règle avec « settings logo --file <image> ».");
        }

        if (key == SettingKeys.ThemeBackground)
        {
            return CliSupport.Fail(output, "L'image de fond se règle avec « settings background --file <image> ».");
        }

        var value = SettingKeys.Normalize(key, options.TryGetValue("value", out var given) ? given : await input.ReadToEndAsync());

        if (SettingKeys.Validate(key, value) is { } error)
        {
            return CliSupport.Fail(output, error);
        }

        await SaveAsync(db, key, value);
        await output.WriteLineAsync($"Paramètre « {key} » enregistré.");
        return 0;
    }

    private static async Task<int> ImageAsync(CseFormsDbContext db, string key, string refusal, string saved, Dictionary<string, string> options, TextWriter output)
    {
        var image = SettingKeys.ImageKeys[key];
        var path = CliSupport.Required(options, Usage, "file");
        if (!File.Exists(path))
        {
            return CliSupport.Fail(output, "Fichier introuvable.");
        }

        // Taille contrôlée avant lecture : un gros fichier n'est jamais chargé en mémoire.
        var dataUri = new FileInfo(path).Length > image.MaxBytes ? null : image.ToDataUri(await File.ReadAllBytesAsync(path));
        if (dataUri is null)
        {
            return CliSupport.Fail(output, $"{refusal} : {image.Rule}.");
        }

        await SaveAsync(db, key, dataUri);
        await output.WriteLineAsync(saved);
        return 0;
    }

    private static async Task SaveAsync(CseFormsDbContext db, string key, string value)
    {
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
            var value = !values.TryGetValue(key, out var v) ? "(non défini)"
                : SettingKeys.ImageKeys.ContainsKey(key) ? $"(image, {Math.Max(1, SettingImage.DecodedSize(v) / 1024)} Ko)"
                : v.Replace("\n", " / ", StringComparison.Ordinal);
            await output.WriteLineAsync($"{key} | {value}");
        }

        return 0;
    }

    private static string KnownKey(Dictionary<string, string> options)
    {
        var key = CliSupport.Required(options, Usage, "key").Trim();
        return SettingKeys.IsKnown(key) ? key : throw new KeyNotFoundException($"Clé inconnue.{Environment.NewLine}{Usage}");
    }
}
