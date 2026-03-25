using Anela.Heblo.Application.Features.ExpeditionList.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SendGrid;

namespace Anela.Heblo.Application.Features.ExpeditionList;

public static class ExpeditionListModule
{
    public static IServiceCollection AddExpeditionListModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PrintPickingListOptions>(configuration.GetSection(PrintPickingListOptions.ConfigurationKey));

        var apiKey = configuration[$"{PrintPickingListOptions.ConfigurationKey}:{nameof(PrintPickingListOptions.SendGridApiKey)}"];
        services.AddSingleton<ISendGridClient>(new SendGridClient(apiKey ?? string.Empty));

        services.AddScoped<IExpeditionListService, ExpeditionListService>();

        // PrintPickingListJob is auto-discovered via IRecurringJob scan in AddRecurringJobs()

        return services;
    }
}
