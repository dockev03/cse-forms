namespace CseForms.Api.Cli;

/// <summary>
/// Point d'entrée et outils communs des commandes d'administration :
/// <c>dotnet CseForms.Api.dll users|forms|sites|settings|notifications ...</c>.
/// </summary>
internal static class CliSupport
{
    private static readonly string[] Commands = ["users", "forms", "sites", "settings", "notifications", "db"];

    public static bool IsCommand(string[] args) => args.Length > 0 && Commands.Contains(args[0]);

    public static Task<int> RunAsync(IServiceProvider services, string[] args, TextReader input, TextWriter output, bool interactive) =>
        args[0] switch
        {
            "users" => UserCommands.RunAsync(services, args, input, output, interactive),
            "forms" => FormCommands.RunAsync(services, args, input, output, interactive),
            "sites" => SiteCommands.RunAsync(services, args, output),
            "notifications" => NotificationCommands.RunAsync(services, args, output),
            "db" => DatabaseCommands.RunAsync(services, args, output),
            _ => SettingCommands.RunAsync(services, args, input, output),
        };

    /// <summary>
    /// Saisie masquée et confirmée au terminal ; lue sur l'entrée standard hors terminal.
    /// Null si les deux saisies diffèrent.
    /// </summary>
    public static string? ReadSecret(TextReader input, TextWriter output, bool interactive, string prompt)
    {
        if (!interactive)
        {
            return input.ReadLine() ?? string.Empty;
        }

        var first = ReadHidden(output, prompt);
        var second = ReadHidden(output, "Confirmation : ");
        return first == second ? first : null;
    }

    public static Dictionary<string, string> ParseOptions(IEnumerable<string> args)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        string? pending = null;
        foreach (var arg in args)
        {
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                pending = arg[2..];
            }
            else if (pending is not null)
            {
                options[pending] = arg;
                pending = null;
            }
        }

        return options;
    }

    public static string Required(Dictionary<string, string> options, string usage, string name) =>
        options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new KeyNotFoundException($"Option --{name} manquante.{Environment.NewLine}{usage}");

    public static int Fail(TextWriter output, string message)
    {
        output.WriteLine(message);
        return 1;
    }

    private static string ReadHidden(TextWriter output, string prompt)
    {
        output.Write(prompt);
        var buffer = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                output.WriteLine();
                return buffer.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0)
                {
                    buffer.Length--;
                }
            }
            else if (!char.IsControl(key.KeyChar))
            {
                buffer.Append(key.KeyChar);
            }
        }
    }
}
