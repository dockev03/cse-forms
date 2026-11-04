using CseForms.Application.FormAdministration;

namespace CseForms.Api.Cli;

/// <summary>
/// Publication des formulaires en ligne de commande, en attendant l'éditeur visuel (lot 5) :
/// <c>forms import|access-code|list</c>. Le code d'accès n'est jamais passé en argument :
/// il est saisi au terminal (ou lu sur l'entrée standard).
/// </summary>
internal static class FormCommands
{
    public const string Usage = """
        Utilisation :
          forms import      --file <fichier.json>   crée le formulaire ou publie une nouvelle version
          forms access-code --form <slug>           change le code d'accès
          forms list
        Le code d'accès (4 à 12 chiffres) est demandé au terminal, ou lu sur l'entrée standard.
        """;

    private const string PublishedBy = "cli";

    public static async Task<int> RunAsync(IServiceProvider services, string[] args, TextReader input, TextWriter output, bool interactive)
    {
        await using var scope = services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<FormAdminService>();
        var options = CliSupport.ParseOptions(args.Skip(2));

        try
        {
            return args.ElementAtOrDefault(1) switch
            {
                "import" => await ImportAsync(service, options, input, output, interactive),
                "access-code" => await AccessCodeAsync(service, options, input, output, interactive),
                "list" => await ListAsync(service, output),
                _ => CliSupport.Fail(output, Usage),
            };
        }
        catch (KeyNotFoundException ex)
        {
            return CliSupport.Fail(output, ex.Message);
        }
    }

    private static async Task<int> ImportAsync(
        FormAdminService service,
        Dictionary<string, string> options,
        TextReader input,
        TextWriter output,
        bool interactive)
    {
        var path = CliSupport.Required(options, Usage, "file");
        if (!File.Exists(path))
        {
            return CliSupport.Fail(output, "Fichier introuvable.");
        }

        // Le modèle PDF éventuel est lu dans le même dossier ; son nom ne peut pas contenir de chemin.
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var read = FormPackageReader.Read(await File.ReadAllTextAsync(path), name =>
        {
            var companion = Path.Combine(directory, name);
            return File.Exists(companion) ? File.ReadAllText(companion) : null;
        });
        if (!read.IsValid)
        {
            await output.WriteLineAsync("Définition refusée :");
            foreach (var error in read.Errors)
            {
                await output.WriteLineAsync($"  {error.Path} : {error.Message}");
            }

            return 1;
        }

        var outcome = await service.ImportAsync(read.Package!, accessCode: null, PublishedBy, CancellationToken.None);
        if (outcome is ImportOutcome.AccessCodeRequired)
        {
            await output.WriteLineAsync("Ce formulaire est protégé par un code d'accès, qui n'est pas encore défini.");
            var code = CliSupport.ReadSecret(input, output, interactive, "Code d'accès : ");
            if (code is null)
            {
                return CliSupport.Fail(output, "Les deux saisies du code diffèrent.");
            }

            outcome = await service.ImportAsync(read.Package!, code, PublishedBy, CancellationToken.None);
        }

        switch (outcome)
        {
            case ImportOutcome.Published published:
                await output.WriteLineAsync(published.Created
                    ? $"Formulaire « {published.Slug} » créé et publié en version {published.Version}."
                    : $"Formulaire « {published.Slug} » publié en version {published.Version}.");
                return 0;
            case ImportOutcome.Unchanged unchanged:
                await output.WriteLineAsync($"Définition inchangée : « {unchanged.Slug} » reste en version {unchanged.Version}.");
                return 0;
            case ImportOutcome.Invalid invalid:
                return CliSupport.Fail(output, invalid.Message);
            default:
                return CliSupport.Fail(output, $"Code d'accès absent ou invalide ({FormAdminService.AccessCodeRule}).");
        }
    }

    private static async Task<int> AccessCodeAsync(
        FormAdminService service,
        Dictionary<string, string> options,
        TextReader input,
        TextWriter output,
        bool interactive)
    {
        var slug = CliSupport.Required(options, Usage, "form");
        var code = CliSupport.ReadSecret(input, output, interactive, "Nouveau code d'accès : ");
        if (code is null)
        {
            return CliSupport.Fail(output, "Les deux saisies du code diffèrent.");
        }

        return await service.SetAccessCodeAsync(slug, code, CancellationToken.None) switch
        {
            AccessCodeOutcome.Updated => Success(output, "Code d'accès modifié. Pensez à réimprimer le flyer."),
            AccessCodeOutcome.NotFound => CliSupport.Fail(output, "Formulaire introuvable."),
            _ => CliSupport.Fail(output, $"Le code d'accès doit comporter {FormAdminService.AccessCodeRule}."),
        };
    }

    private static async Task<int> ListAsync(FormAdminService service, TextWriter output)
    {
        foreach (var form in await service.ListAsync(CancellationToken.None))
        {
            var retention = form.RetentionDays is { } days ? $"{days} j" : "conservation non définie";
            await output.WriteLineAsync(
                $"{form.Slug} | {form.Title} | {form.Status} | v{form.Version?.ToString() ?? "-"} | {form.CaseNumberPrefix} | "
                + $"{(form.HasAccessCode ? "code d'accès" : "public")} | {retention}");
        }

        return 0;
    }

    private static int Success(TextWriter output, string message)
    {
        output.WriteLine(message);
        return 0;
    }
}
