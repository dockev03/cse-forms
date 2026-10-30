using CseForms.Application.Common;
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

        return services;
    }
}
