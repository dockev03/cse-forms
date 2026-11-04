using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;

namespace CseForms.E2E.Tests.Infrastructure;

/// <summary>
/// Lance une application compilée de la solution (<c>dotnet CseForms.Api.dll</c>, <c>CseForms.Worker.dll</c>)
/// dans un processus séparé et garde sa sortie pour le diagnostic d'un échec.
/// </summary>
public sealed class AppProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly ConcurrentQueue<string> _output = new();

    private AppProcess(Process process)
    {
        _process = process;
        _process.OutputDataReceived += (_, e) => Keep(e.Data);
        _process.ErrorDataReceived += (_, e) => Keep(e.Data);
    }

    public string Output => string.Join(Environment.NewLine, _output);

    public bool HasExited => _process.HasExited;

    /// <summary>Chemin de l'assembly compilée d'un projet de <c>src</c>, dans la même configuration que les tests.</summary>
    public static string AssemblyPath(string project)
    {
        var configuration = typeof(AppProcess).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "BuildConfiguration").Value!;
        var path = Path.Combine(SolutionRoot, "src", project, "bin", configuration, "net10.0", $"{project}.dll");
        return File.Exists(path) ? path : throw new FileNotFoundException($"{project} n'est pas compilé.", path);
    }

    public static string SolutionRoot { get; } = FindSolutionRoot();

    public static AppProcess Start(string assembly, IReadOnlyDictionary<string, string> environment, params string[] args)
    {
        var process = new Process { StartInfo = CreateStartInfo(assembly, environment, args) };
        var app = new AppProcess(process);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return app;
    }

    /// <summary>Exécute une commande d'administration et renvoie sa sortie ; échoue si le code de sortie n'est pas 0.</summary>
    public static async Task<string> RunAsync(
        string assembly, IReadOnlyDictionary<string, string> environment, string stdin, params string[] args)
    {
        var startInfo = CreateStartInfo(assembly, environment, args);
        startInfo.RedirectStandardInput = true;
        using var process = Process.Start(startInfo)!;
        await process.StandardInput.WriteLineAsync(stdin);
        process.StandardInput.Close();

        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        await process.WaitForExitAsync(timeout.Token);

        var text = await output + await error;
        return process.ExitCode == 0
            ? text
            : throw new InvalidOperationException($"Commande « {string.Join(' ', args.Take(2))} » en échec ({process.ExitCode}) :{Environment.NewLine}{text}");
    }

    public async ValueTask DisposeAsync()
    {
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
        }

        _process.Dispose();
    }

    private void Keep(string? line)
    {
        if (line is null)
        {
            return;
        }

        _output.Enqueue(line);
        while (_output.Count > 400 && _output.TryDequeue(out _))
        {
        }
    }

    private static ProcessStartInfo CreateStartInfo(string assembly, IReadOnlyDictionary<string, string> environment, string[] args)
    {
        // Racine de contenu : le dossier du projet (wwwroot, appsettings.json), comme avec `dotnet run`.
        var project = Path.GetFileNameWithoutExtension(assembly);
        var contentRoot = Path.Combine(SolutionRoot, "src", project);
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = contentRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(assembly);
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        foreach (var (name, value) in environment)
        {
            startInfo.Environment[name] = value;
        }

        startInfo.Environment["ASPNETCORE_CONTENTROOT"] = contentRoot;
        startInfo.Environment["DOTNET_CONTENTROOT"] = contentRoot;

        return startInfo;
    }

    private static string FindSolutionRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CseForms.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Racine de la solution introuvable.");
    }
}
