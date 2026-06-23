using Anela.Heblo.Application.Features.ExpeditionList.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.ExpeditionList.Services;
using Anela.Heblo.Xcc.Services.BackgroundRefresh;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.ExpeditionList;

public static class ExpeditionListModule
{
    public static IServiceCollection AddExpeditionListModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PrintPickingListOptions>(configuration.GetSection(PrintPickingListOptions.ConfigurationKey));

        services.AddScoped<IExpeditionListService, ExpeditionListService>();

        // Automatic "Tisk-Robot" print, scheduled/visible/manually-triggerable via the BackgroundRefresh
        // registry (config under BackgroundRefresh:IExpeditionListService:AutoPrintPickingList).
        // Production-only auto-run; manual force-refresh works in all environments.
        services.RegisterRefreshTask(
            nameof(IExpeditionListService),
            "AutoPrintPickingList",
            async (sp, ct) =>
            {
                var service = sp.GetRequiredService<IExpeditionListService>();
                var options = sp.GetRequiredService<IOptions<PrintPickingListOptions>>().Value;
                await AutoPrintPickingListTask.ExecuteOnceAsync(service, options, ct);
            });

        // PrintPickingListJob is auto-discovered via IRecurringJob scan in AddRecurringJobs()

        return services;
    }
}
