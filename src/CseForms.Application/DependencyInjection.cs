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
        services.AddScoped<Retention.SubmissionEraser>();
        services.AddScoped<Retention.RetentionService>();
        services.AddScoped<FormAdminService>();
        services.AddScoped<FormLifecycleService>();
        services.AddScoped<FormImportService>();
        services.AddScoped<FormDraftService>();
        services.AddScoped<FormDuplicationService>();
        services.AddScoped<FormTemplateService>();
        services.AddScoped<Notifications.NotificationRuleService>();
        services.AddScoped<Distribution.DistributionService>();
        services.AddScoped<Settings.SettingsAdminService>();

        return services;
    }
}
