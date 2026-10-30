using CseForms.Infrastructure;
using CseForms.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks()
    .AddDbContextCheck<CseFormsDbContext>("database", tags: ["ready"]);

var app = builder.Build();

// Vivacité : le processus répond, sans dépendance externe.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });

// Disponibilité : la base est joignable. Seul l'état global est renvoyé, sans détail technique.
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.Run();

public partial class Program;
