using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CseForms.Testing;
using Microsoft.Playwright;

namespace CseForms.E2E.Tests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class E2ECollection : ICollectionFixture<E2EEnvironment>
{
    public const string Name = "E2E";
}

/// <summary>
/// Application complète pour les tests de bout en bout, démarrée une fois pour toute la collection :
/// base PostgreSQL jetable, API et Worker en processus séparés, Gotenberg, serveur SMTP factice, navigateur Chromium.
/// Les données sont créées par la ligne de commande d'administration, comme en production.
/// Uniquement des données fictives ; mots de passe et code d'accès tirés au hasard à chaque exécution.
/// </summary>
public sealed class E2EEnvironment : IAsyncLifetime
{
    /// <summary>Adresse de Gotenberg (par défaut celle de <c>docker-compose.dev.yml</c>).</summary>
    public const string GotenbergVariable = "CSEFORMS_TEST_GOTENBERG";

    /// <summary>Chemin d'un Chromium déjà installé, à la place de celui de Playwright (facultatif).</summary>
    public const string ChromiumVariable = "CSEFORMS_E2E_CHROMIUM";

    public const string FormSlug = "inscription";

    public static readonly string[] Sites = ["Abrest", "Riom"];

    /// <summary>Destinataire des notifications du formulaire d'inscription.</summary>
    public const string NotificationRecipient = "elus.e2e@example.test";

    /// <summary>Adresse publique déclarée dans les paramètres (liens des emails).</summary>
    public const string PublicBaseUrl = "https://formulaires.example.test";

    private readonly List<string> _directories = [];
    private PostgresFixture? _database;
    private AppProcess? _api;
    private AppProcess? _worker;
    private IPlaywright? _playwright;
    private FakeSmtpServer? _smtp;
    private Dictionary<string, string> _environment = [];

    /// <summary>Emails envoyés par le Worker.</summary>
    public FakeSmtpServer Smtp => _smtp!;

    public string BaseUrl { get; private set; } = string.Empty;

    public string AccessCode { get; } = RandomNumberGenerator.GetInt32(100_000, 1_000_000).ToString(System.Globalization.CultureInfo.InvariantCulture);

    public Account Admin { get; } = Account.Create("admin.e2e@example.test", "Admin Fictif", "admin");

    /// <summary>Lecteur habilité sur le formulaire d'inscription.</summary>
    public Account Reader { get; } = Account.Create("lecteur.e2e@example.test", "Lecteur Fictif", "reader");

    /// <summary>Lecteur sans aucun droit sur le formulaire.</summary>
    public Account Outsider { get; } = Account.Create("sansdroit.e2e@example.test", "Autre Fictif", "reader");

    public IBrowser Browser { get; private set; } = null!;

    public IPlaywright Playwright => _playwright!;

    /// <summary>Sorties de l'API et du Worker, jointes au message d'un test en échec.</summary>
    public string ServerOutput =>
        $"--- API ---{Environment.NewLine}{_api?.Output}{Environment.NewLine}--- Worker ---{Environment.NewLine}{_worker?.Output}";

    public async Task InitializeAsync()
    {
        if (!E2EFactAttribute.Enabled)
        {
            return;
        }

        _database = new PostgresFixture();
        await _database.InitializeAsync();
        _smtp = new FakeSmtpServer();

        var environment = new Dictionary<string, string>
        {
            ["DOTNET_ENVIRONMENT"] = "Production",
            ["ASPNETCORE_ENVIRONMENT"] = "Production",
            ["ConnectionStrings__Default"] = _database.ConnectionString,
            ["Storage__Path"] = TempDirectory("files"),
            ["Storage__EncryptionKey"] = TestData.StorageKey,
            ["ProofOfWork__HmacKey"] = ProofOfWorkClient.TestKey,
            ["DataProtection__KeysPath"] = TempDirectory("keys"),
            ["RateLimiting__PublicSubmit__PermitLimit"] = "1000",
            ["RateLimiting__PublicRead__PermitLimit"] = "1000",
            ["RateLimiting__Login__PermitLimit"] = "1000",
            ["Gotenberg__Url"] = Environment.GetEnvironmentVariable(GotenbergVariable) ?? "http://127.0.0.1:3000",
            ["Jobs__PollSeconds"] = "1",
            ["Jobs__WorkerCount"] = "1",
            // Journaux réduits aux avertissements : ils sont joints au rapport d'un test en échec.
            ["Logging__LogLevel__Default"] = "Warning",
        };

        _environment = environment;
        await SeedAsync(environment);

        // HTTPS obligatoire : cookies « Secure » et jeton anti-CSRF du back-office.
        BaseUrl = $"https://localhost:{FreePort()}";
        var apiEnvironment = new Dictionary<string, string>(environment)
        {
            ["Kestrel__Certificates__Default__Path"] = CreateCertificate(out var certificatePassword),
            ["Kestrel__Certificates__Default__Password"] = certificatePassword,
        };
        _api = AppProcess.Start(AppProcess.AssemblyPath("CseForms.Api"), apiEnvironment, "--urls", BaseUrl);
        _worker = AppProcess.Start(AppProcess.AssemblyPath("CseForms.Worker"), environment);
        await WaitUntilReadyAsync();

        _playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            ExecutablePath = Environment.GetEnvironmentVariable(ChromiumVariable) is { Length: > 0 } path ? path : null,
        });
    }

    public async Task DisposeAsync()
    {
        if (_playwright is not null)
        {
            await Browser.DisposeAsync();
            _playwright.Dispose();
        }

        if (_worker is not null)
        {
            await _worker.DisposeAsync();
        }

        if (_api is not null)
        {
            await _api.DisposeAsync();
        }

        if (_database is not null)
        {
            await _database.DisposeAsync();
        }

        if (_smtp is not null)
        {
            await _smtp.DisposeAsync();
        }

        foreach (var directory in _directories)
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Commande d'administration du Worker, avec sa configuration ; lève une exception si elle échoue.</summary>
    public Task<string> RunWorkerCommandAsync(params string[] args) =>
        AppProcess.RunAsync(AppProcess.AssemblyPath("CseForms.Worker"), _environment, string.Empty, args);

    private async Task SeedAsync(Dictionary<string, string> environment)
    {
        var cli = AppProcess.AssemblyPath("CseForms.Api");
        foreach (var site in Sites)
        {
            await AppProcess.RunAsync(cli, environment, string.Empty, "sites", "add", "--label", site);
        }

        var definition = Path.Combine(AppProcess.SolutionRoot, "forms", "inscription.json");
        await AppProcess.RunAsync(cli, environment, AccessCode, "forms", "import", "--file", definition);

        foreach (var account in new[] { Admin, Reader, Outsider })
        {
            await AppProcess.RunAsync(cli, environment, account.Password,
                "users", "create", "--email", account.Email, "--name", account.Name, "--role", account.Role);
        }

        await AppProcess.RunAsync(cli, environment, string.Empty, "users", "grant", "--email", Reader.Email, "--form", FormSlug);

        // Emails : serveur SMTP factice local, sans chiffrement ni authentification.
        var settings = new Dictionary<string, string>
        {
            ["smtp.host"] = "127.0.0.1",
            ["smtp.port"] = _smtp!.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["smtp.security"] = "none",
            ["smtp.from"] = "formulaires.e2e@example.test",
            ["app.publicBaseUrl"] = PublicBaseUrl,
            ["cse.name"] = "CSE Démo",
            ["cse.email"] = "cse.e2e@example.test",
        };
        foreach (var (key, value) in settings)
        {
            await AppProcess.RunAsync(cli, environment, string.Empty, "settings", "set", "--key", key, "--value", value);
        }

        await AppProcess.RunAsync(cli, environment, string.Empty, "notifications", "set", "--form", FormSlug,
            "--to", NotificationRecipient, "--subject", "Nouvelle inscription n° {numero}, site {site}");
    }

    private async Task WaitUntilReadyAsync()
    {
        using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator };
        using var http = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) };
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            if (_api!.HasExited || _worker!.HasExited)
            {
                throw new InvalidOperationException($"L'application s'est arrêtée au démarrage.{Environment.NewLine}{ServerOutput}");
            }

            try
            {
                if ((await http.GetAsync("/health/ready")).IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Pas encore à l'écoute.
            }

            await Task.Delay(250);
        }

        throw new TimeoutException($"L'API n'a pas démarré en 60 secondes.{Environment.NewLine}{ServerOutput}");
    }

    private string TempDirectory(string name)
    {
        var path = Directory.CreateTempSubdirectory($"cse-forms-e2e-{name}-").FullName;
        _directories.Add(path);
        return path;
    }

    /// <summary>Certificat auto-signé pour localhost, valable une journée, protégé par un mot de passe tiré au hasard.</summary>
    private string CreateCertificate(out string password)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        request.CertificateExtensions.Add(names.Build());
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));

        password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(18));
        var path = Path.Combine(TempDirectory("tls"), "localhost.pfx");
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, password));
        return path;
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}

public sealed record Account(string Email, string Name, string Role, string Password)
{
    public static Account Create(string email, string name, string role) =>
        new(email, name, role, Convert.ToBase64String(RandomNumberGenerator.GetBytes(18)));
}
