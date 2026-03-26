using Anela.Heblo.Application.Features.ExpeditionList.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.ExpeditionList;

public static class ExpeditionListModule
{
    public static IServiceCollection AddExpeditionListModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PrintPickingListOptions>(configuration.GetSection(PrintPickingListOptions.ConfigurationKey));

        services.AddScoped<IExpeditionListService, ExpeditionListService>();

        // PrintPickingListJob is auto-discovered via IRecurringJob scan in AddRecurringJobs()

        return services;
    }
}
