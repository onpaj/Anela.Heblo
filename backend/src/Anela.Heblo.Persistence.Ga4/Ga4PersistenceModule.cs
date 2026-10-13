using Anela.Heblo.Persistence.Infrastructure.Resilience;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Anela.Heblo.Persistence.Ga4;

public static class Ga4PersistenceModule
{
    /// <summary>
    /// Registers <see cref="Ga4DbContext"/> against its own small connection pool.
    ///
    /// It gets a dedicated NpgsqlDataSource (keyed, so DI disposes it without shadowing the main
    /// singleton) and its own resilience pipeline, mirroring AnalyticsPersistenceModule: the GA4
    /// sync SaveChangesAsync's whole batches at once and can legitimately outrun the short
    /// per-attempt timeout that the request-serving pipeline is tuned for.
    /// </summary>
    public static IServiceCollection AddGa4PersistenceServices(
        this IServiceCollection services,
        string connectionString,
        int maxPoolSize)
    {
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
        dataSourceBuilder.ConnectionStringBuilder.KeepAlive = 30;
        dataSourceBuilder.ConnectionStringBuilder.ConnectionLifetime = 600;
        dataSourceBuilder.ConnectionStringBuilder.MaxPoolSize = maxPoolSize;
        var dataSource = dataSourceBuilder.Build();

        services.AddKeyedSingleton<NpgsqlDataSource>("ga4", dataSource);

        services.AddKeyedSingleton<IDbResiliencePipelineProvider>("ga4", (sp, _) =>
            new DbResiliencePipelineProvider(
                Options.Create(new DbResilienceOptions()),
                sp.GetRequiredService<DbResilienceMetrics>(),
                sp.GetService<ILogger<DbResiliencePipelineProvider>>()
                    ?? NullLogger<DbResiliencePipelineProvider>.Instance));

        services.AddDbContext<Ga4DbContext>((sp, options) =>
        {
            options.UseNpgsql(dataSource, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__EFMigrationsHistory", Ga4DbContext.SchemaName);
                npgsql.ExecutionStrategy(deps =>
                    new PollyExecutionStrategy(
                        deps,
                        sp.GetRequiredKeyedService<IDbResiliencePipelineProvider>("ga4"),
                        sp.GetRequiredService<DbResilienceMetrics>(),
                        sp.GetRequiredService<ILogger<PollyExecutionStrategy>>()));
            });
            options.AddInterceptors(sp.GetRequiredService<NpgsqlConnectionInterceptor>());
        });

        return services;
    }
}
