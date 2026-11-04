using CseForms.Application.BackOffice;
using CseForms.Application.Common;
using CseForms.Application.FormAdministration;
using CseForms.Application.PublicForms;
using Microsoft.Extensions.DependencyInjection;

namespace CseForms.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<CseCalendar>();
        services.AddScoped<PublicFormService>();
        services.AddScoped<BackOfficeService>();
        services.AddScoped<FormAdminService>();

        return services;
    }
}
