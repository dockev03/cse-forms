using CseForms.Application;
using CseForms.Application.Jobs;
using CseForms.Application.Notifications;
using CseForms.Application.Pdf;
using CseForms.Infrastructure;
using CseForms.Worker.Cli;
using CseForms.Worker.Jobs;
using Hangfire;
using Hangfire.PostgreSql;

// Commande d'administration : test des réglages SMTP, sans démarrer la file de tâches.
if (MailCommand.IsCommand(args))
{
    var cliBuilder = Host.CreateApplicationBuilder();
    cliBuilder.Logging.ClearProviders();
    cliBuilder.Services.AddApplication();
    cliBuilder.Services.AddInfrastructure(cliBuilder.Configuration);
    cliBuilder.Services.AddEmailSending(cliBuilder.Configuration);
    using var cliHost = cliBuilder.Build();
    return await MailCommand.RunAsync(cliHost.Services, args, Console.Out, TimeProvider.System);
}

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddPdfConversion(builder.Configuration);
builder.Services.AddEmailSending(builder.Configuration);
builder.Services.AddScoped<IJobFailureAlerter, EmailJobFailureAlerter>();
builder.Services.AddScoped<SubmissionPdfService>();
builder.Services.AddScoped<SubmissionEmailService>();

// File de tâches Hangfire, stockée dans le schéma « hangfire » de la même base. Aucun tableau de bord exposé.
var connectionString = builder.Configuration.GetConnectionString(CseForms.Infrastructure.DependencyInjection.ConnectionStringName)!;
builder.Services.AddHangfire(configuration => configuration
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(
        options => options.UseNpgsqlConnection(connectionString),
        new PostgreSqlStorageOptions { SchemaName = "hangfire", PrepareSchemaIfNecessary = true }));
builder.Services.AddHangfireServer(options => options.WorkerCount = Math.Clamp(builder.Configuration.GetValue("Jobs:WorkerCount", 2), 1, 20));

builder.Services.AddScoped<PdfJob>();
builder.Services.AddScoped<EmailJob>();
builder.Services.AddScoped<PurgeJob>();
builder.Services.AddHostedService<JobDispatcherService>();

var host = builder.Build();

// Purge automatique des réponses (durée de conservation de chaque formulaire, comptée à partir du traitement).
PurgeJob.Schedule(host.Services.GetRequiredService<IRecurringJobManager>(), builder.Configuration);

await host.RunAsync();
return 0;
