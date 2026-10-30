using CseForms.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);

// Les tâches (PDF, emails, purge) seront ajoutées avec Hangfire dans une étape ultérieure.
builder.Services.AddInfrastructure(builder.Configuration);

var host = builder.Build();
host.Run();
