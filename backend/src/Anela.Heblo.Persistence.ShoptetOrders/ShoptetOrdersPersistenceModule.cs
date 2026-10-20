using Anela.Heblo.Persistence.Infrastructure.Resilience;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Anela.Heblo.Persistence.ShoptetOrders;

public static class ShoptetOrdersPersistenceModule
{
    private const string ServiceKey = "shoptet-orders";

    public static IServiceCollection AddShoptetOrdersPersistenceServices(
        this IServiceCollection services,
        string connectionString,
        int maxPoolSize)
    {
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.ConnectionStringBuilder.KeepAlive = 30;
        dataSourceBuilder.ConnectionStringBuilder.ConnectionLifetime = 600;
        dataSourceBuilder.ConnectionStringBuilder.MaxPoolSize = maxPoolSize;
        var dataSource = dataSourceBuilder.Build();

        // Keyed so DI owns disposal without shadowing the main NpgsqlDataSource singleton,
        // exactly as AnalyticsPersistenceModule does for flexi_raw.
        services.AddKeyedSingleton<NpgsqlDataSource>(ServiceKey, dataSource);

        // The order sync SaveChangesAsync's whole batches of orders plus their lines and can
        // legitimately run past the request-serving pipeline's short per-attempt timeout, so it
        // gets its own pipeline with the un-tuned DbResilienceOptions defaults.
        services.AddKeyedSingleton<IDbResiliencePipelineProvider>(ServiceKey, (sp, _) =>
            new DbResiliencePipelineProvider(
                Options.Create(new DbResilienceOptions()),
                sp.GetRequiredService<DbResilienceMetrics>(),
                sp.GetService<ILogger<DbResiliencePipelineProvider>>()
                    ?? NullLogger<DbResiliencePipelineProvider>.Instance));

        services.AddDbContext<ShoptetOrdersDbContext>((sp, options) =>
        {
            options.UseNpgsql(dataSource, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", ShoptetOrdersDbContext.SchemaName);
                npgsql.ExecutionStrategy(deps =>
                    new PollyExecutionStrategy(
                        deps,
                        sp.GetRequiredKeyedService<IDbResiliencePipelineProvider>(ServiceKey),
                        sp.GetRequiredService<DbResilienceMetrics>(),
                        sp.GetRequiredService<ILogger<PollyExecutionStrategy>>()));
            });
            options.AddInterceptors(sp.GetRequiredService<NpgsqlConnectionInterceptor>());
        });
        return services;
    }
}
