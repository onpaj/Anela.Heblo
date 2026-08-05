using Anela.Heblo.Persistence.Infrastructure.Resilience;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Anela.Heblo.Persistence.Analytics;

public static class AnalyticsPersistenceModule
{
    public static IServiceCollection AddAnalyticsPersistenceServices(
        this IServiceCollection services,
        string connectionString,
        int maxPoolSize)
    {
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.ConnectionStringBuilder.KeepAlive = 30;
        dataSourceBuilder.ConnectionStringBuilder.ConnectionLifetime = 600;
        dataSourceBuilder.ConnectionStringBuilder.MaxPoolSize = maxPoolSize;
        var dataSource = dataSourceBuilder.Build();

        // Register with a key so DI manages disposal without shadowing the main NpgsqlDataSource singleton
        // (which is used by EanCodeGenerator and health checks).
        services.AddKeyedSingleton<NpgsqlDataSource>("analytics", dataSource);

        // AnalyticsDbContext backs LedgerSyncService.UpsertBatchAsync (and siblings), which SaveChangesAsync's
        // up to FlexiAnalyticsSyncOptions.BatchSize=500 rows in a single call and can legitimately run past
        // the Database:Resilience-configured, request-serving pipeline's short per-attempt timeout. It gets
        // its own pipeline here, keeping DbResilienceOptions' un-tuned defaults, staying isolated from that
        // config section instead of sharing the singleton IDbResiliencePipelineProvider.
        services.AddKeyedSingleton<IDbResiliencePipelineProvider>("analytics", (sp, _) =>
            new DbResiliencePipelineProvider(
                Options.Create(new DbResilienceOptions()),
                sp.GetRequiredService<DbResilienceMetrics>(),
                sp.GetService<ILogger<DbResiliencePipelineProvider>>()
                    ?? NullLogger<DbResiliencePipelineProvider>.Instance));

        services.AddDbContext<AnalyticsDbContext>((sp, options) =>
        {
            options.UseNpgsql(dataSource, npgsql =>
            {
                npgsql.ExecutionStrategy(deps =>
                    new PollyExecutionStrategy(
                        deps,
                        sp.GetRequiredKeyedService<IDbResiliencePipelineProvider>("analytics"),
                        sp.GetRequiredService<DbResilienceMetrics>(),
                        sp.GetRequiredService<ILogger<PollyExecutionStrategy>>()));
            });
            options.AddInterceptors(sp.GetRequiredService<NpgsqlConnectionInterceptor>());
        });
        return services;
    }
}
