using CseForms.Api.Endpoints;
using CseForms.Api.Hosting;
using CseForms.Application;
using CseForms.Infrastructure;
using CseForms.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddReverseProxySupport(builder.Configuration);
builder.Services.AddPublicRateLimiting(builder.Configuration);
builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
});
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.MaxDepth = 32);
builder.Services.AddHealthChecks()
    .AddDbContextCheck<CseFormsDbContext>("database", tags: ["ready"]);

var app = builder.Build();

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

// Erreur inattendue : réponse générique, le détail ne part que dans les journaux.
app.UseExceptionHandler(errorApp => errorApp.Run(context =>
{
    // Requête mal formée ou trop volumineuse (Kestrel) : on garde son code 4xx.
    if (context.Features.Get<IExceptionHandlerFeature>()?.Error is BadHttpRequestException badRequest)
    {
        return ApiErrors.Error(badRequest.StatusCode, "badRequest", "Requête invalide.").ExecuteAsync(context);
    }

    return ApiErrors
        .Error(StatusCodes.Status500InternalServerError, "serverError", "Une erreur est survenue. Réessayez plus tard.")
        .ExecuteAsync(context);
}));
app.UseStatusCodePages(context => context.HttpContext.Response.StatusCode == StatusCodes.Status400BadRequest
    ? ApiErrors.BadRequest().ExecuteAsync(context.HttpContext)
    : Task.CompletedTask);

app.UseApiSecurityHeaders();
app.UsePublicAssets();
app.UseRouting();
app.UseRateLimiter();
app.UseRequestSizeGuard();

// Vivacité : le processus répond, sans dépendance externe.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });

// Disponibilité : la base est joignable. Seul l'état global est renvoyé, sans détail technique.
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.MapPublicFormEndpoints();
app.MapPublicPages(app.Environment);

app.Run();

public partial class Program;
