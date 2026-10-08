using Anela.Heblo.Persistence.Infrastructure.Resilience;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Anela.Heblo.Persistence.Ads;

public static class AdsPersistenceModule
{
    public const string ServiceKey = "ads";

    /// <summary>
    /// Registers <see cref="AdsDbContext"/> against its own small connection pool and its own
    /// resilience pipeline, exactly as Ga4PersistenceModule does: the daily ad sync SaveChangesAsync's
    /// whole batches and can legitimately outrun the request-serving pipeline's short per-attempt
    /// timeout, and a separate pool keeps a long sync from starving request-path connections on the
    /// single-vCore heblosql server. The data source is keyed so it does not shadow the main
    /// NpgsqlDataSource singleton. It is built eagerly (so a bad connection string fails inside the
    /// caller's try/catch) and registered as an instance, which DI does not dispose — the pool lives
    /// for the process lifetime.
    /// </summary>
    public static IServiceCollection AddAdsPersistenceServices(
        this IServiceCollection services,
        string connectionString,
        int maxPoolSize)
    {
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.ConnectionStringBuilder.KeepAlive = 30;
        dataSourceBuilder.ConnectionStringBuilder.ConnectionLifetime = 600;
        dataSourceBuilder.ConnectionStringBuilder.MaxPoolSize = maxPoolSize;
        var dataSource = dataSourceBuilder.Build();

        services.AddKeyedSingleton<NpgsqlDataSource>(ServiceKey, dataSource);

        services.AddKeyedSingleton<IDbResiliencePipelineProvider>(ServiceKey, (sp, _) =>
            new DbResiliencePipelineProvider(
                Options.Create(new DbResilienceOptions()),
                sp.GetRequiredService<DbResilienceMetrics>(),
                sp.GetService<ILogger<DbResiliencePipelineProvider>>()
                    ?? NullLogger<DbResiliencePipelineProvider>.Instance));

        services.AddDbContext<AdsDbContext>((sp, options) =>
        {
            options.UseNpgsql(dataSource, npgsql =>
            {
                npgsql.MigrationsHistoryTable(AdsDbContext.MigrationsHistoryTableName, AdsDbContext.SchemaName);
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
