using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Anela.Heblo.Persistence.Analytics;

public static class AnalyticsPersistenceModule
{
    public static IServiceCollection AddAnalyticsPersistenceServices(
        this IServiceCollection services,
        string connectionString)
    {
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.ConnectionStringBuilder.KeepAlive = 30;
        dataSourceBuilder.ConnectionStringBuilder.ConnectionLifetime = 600;
        var dataSource = dataSourceBuilder.Build();

        // Do not register dataSource as a bare NpgsqlDataSource singleton — it would shadow the main application pool
        // and break EanCodeGenerator and health checks. Pass it only to AddDbContext via closure.
        services.AddDbContext<AnalyticsDbContext>(options => options.UseNpgsql(dataSource));
        return services;
    }
}
