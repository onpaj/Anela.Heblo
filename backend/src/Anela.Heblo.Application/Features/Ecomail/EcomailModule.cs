using Anela.Heblo.Application.Features.Ecomail.Services;
using Anela.Heblo.Domain.Features.Ecomail;
using Anela.Heblo.Persistence.Ecomail;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Anela.Heblo.Application.Features.Ecomail;

public static class EcomailModule
{
    public static IServiceCollection AddEcomailModule(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IEcomailRepository, EcomailRepository>();
        services.AddScoped<IEcomailSyncService, EcomailSyncService>();
        // IRecurringJob implementations are discovered by assembly scan — EcomailSyncJob needs no
        // explicit registration, matching MarketingPerformanceModule.
        return services;
    }
}
