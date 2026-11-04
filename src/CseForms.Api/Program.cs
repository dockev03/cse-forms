using CseForms.Api.BackOffice;
using CseForms.Api.Cli;
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
builder.Services.AddBackOffice(builder.Configuration);
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

// Administration en ligne de commande : `dotnet CseForms.Api.dll users|forms|sites|settings ...`.
if (CliSupport.IsCommand(args))
{
    return await CliSupport.RunAsync(app.Services, args, Console.In, Console.Out, interactive: !Console.IsInputRedirected);
}

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

// Erreur inattendue : réponse générique, le détail ne part que dans les journaux.
app.UseExceptionHandler(errorApp => errorApp.Run(context =>
{
    // Requête mal formée ou trop volumineuse (Kestrel) : on garde son code 4xx.
    var status = context.Features.Get<IExceptionHandlerFeature>()?.Error is BadHttpRequestException badRequest
        ? badRequest.StatusCode
        : StatusCodes.Status500InternalServerError;

    return ErrorResponses.Write(context, status);
}));
app.UseStatusCodePages(context => ErrorResponses.WriteIfEmpty(context.HttpContext));

app.UseApiSecurityHeaders();
app.UsePublicAssets();
app.UseRouting();
app.UseRateLimiter();
app.UseRequestSizeGuard();
app.UseAuthentication();
app.UseAuthorization();

// Vivacité : le processus répond, sans dépendance externe.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });

// Disponibilité : la base est joignable. Seul l'état global est renvoyé, sans détail technique.
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.MapPublicFormEndpoints();
app.MapPublicPages(app.Environment);
app.MapRazorPages();

await app.RunAsync();
return 0;

public partial class Program;
