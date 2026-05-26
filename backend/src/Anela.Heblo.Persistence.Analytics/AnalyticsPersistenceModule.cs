using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Persistence.Analytics;

public static class AnalyticsPersistenceModule
{
    public static IServiceCollection AddAnalyticsPersistenceServices(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<AnalyticsDbContext>(options =>
            options.UseNpgsql(connectionString));
        return services;
    }
}
