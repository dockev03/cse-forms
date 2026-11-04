using CseForms.Application;
using CseForms.Application.Pdf;
using CseForms.Infrastructure;
using CseForms.Worker.Jobs;
using Hangfire;
using Hangfire.PostgreSql;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddPdfConversion(builder.Configuration);
builder.Services.AddScoped<SubmissionPdfService>();

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
builder.Services.AddHostedService<JobDispatcherService>();

var host = builder.Build();
host.Run();
