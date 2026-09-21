using Anela.Heblo.Domain.Accounting.CostPools;
using Anela.Heblo.Xcc.Services.BackgroundRefresh;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Shared.CostPools;

/// <summary>
/// Composition root for cost pool totals - shared across features rather than
/// owned by one, in the same spirit as Shared/Rag and Shared/Users.
/// </summary>
public static class SharedCostPoolsModule
{
    public static IServiceCollection AddSharedCostPoolsModule(this IServiceCollection services)
    {
        services.AddMemoryCache();

        services.AddScoped<ICostPoolCache, CostPoolCache>();
        services.AddScoped<ICostPoolService, CostPoolService>();

        services.RegisterRefreshTask<ICostPoolService>(
            "RefreshCache",
            (service, ct) => service.RefreshAsync(ct));

        return services;
    }
}
