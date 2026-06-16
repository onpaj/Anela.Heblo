using Anela.Heblo.Domain.Features.Catalog.Inventory;
using Anela.Heblo.Persistence.Catalog.Inventory;
using Anela.Heblo.Persistence.Infrastructure;
using Anela.Heblo.Persistence.Infrastructure.Resilience;
using Anela.Heblo.Xcc.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Pgvector;

namespace Anela.Heblo.Persistence;

/// <summary>
/// Extension methods for registering persistence services
/// </summary>
public static class PersistenceModule
{
    public static IServiceCollection AddPersistenceServices(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var connectionString = configuration.GetConnectionString(environment.EnvironmentName);

        if (string.IsNullOrEmpty(connectionString))
        {
            throw new Exception($"No connection string '{environment.EnvironmentName}' found in configuration.");
        }

        var useInMemory = bool.Parse(configuration["UseInMemoryDatabase"] ?? "false");

        // Build NpgsqlDataSource once as a singleton so all DbContext scopes share
        // a single connection pool. Building it inside AddDbContext would create a
        // new pool per DI scope (EF Core 8 default: optionsLifetime = Scoped).
        NpgsqlDataSource? dataSource = null;
        if (!useInMemory && connectionString != "InMemory")
        {
            var maxPoolSize = configuration.GetValue<int?>("Database:MaxPoolSize");
            var connectionIdleLifetime = configuration.GetValue<int?>("Database:ConnectionIdleLifetime");
            var connectionPruningInterval = configuration.GetValue<int?>("Database:ConnectionPruningInterval");

            var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
            if (maxPoolSize.HasValue)
            {
                dataSourceBuilder.ConnectionStringBuilder.MaxPoolSize = maxPoolSize.Value;
            }

            // Prune stale connections left over after a DB restart or maintenance window.
            // Without these settings the pool holds dead sockets until the caller hits a
            // SocketException (observed spike: 112 SocketExceptions in 24 h, 6.1× the
            // 7-day average, coinciding with a pg_terminate_backend / server restart).
            dataSourceBuilder.ConnectionStringBuilder.KeepAlive = 30;         // TCP keepalive every 30 s
            dataSourceBuilder.ConnectionStringBuilder.ConnectionLifetime = 600; // retire any connection older than 10 min

            // Reclaim idle connections faster after burst load to avoid holding
            // connections that block other processes (e.g. migrations, Hangfire).
            if (connectionIdleLifetime.HasValue)
            {
                dataSourceBuilder.ConnectionStringBuilder.ConnectionIdleLifetime = connectionIdleLifetime.Value;
            }

            if (connectionPruningInterval.HasValue)
            {
                dataSourceBuilder.ConnectionStringBuilder.ConnectionPruningInterval = connectionPruningInterval.Value;
            }

            dataSourceBuilder.UseVector();
            dataSource = dataSourceBuilder.Build();
            services.AddSingleton(dataSource); // Register for DI-managed disposal
        }

        // Register material container code generator — real implementation needs NpgsqlDataSource (raw ADO.NET
        // for sequence access); fall back to NullMaterialContainerCodeGenerator when running in-memory so that
        // DI validation and tests can start without a live database.
        if (!useInMemory && connectionString != "InMemory" && dataSource != null)
        {
            services.AddScoped<IMaterialContainerCodeGenerator, MaterialContainerCodeGenerator>();
        }
        else
        {
            services.AddScoped<IMaterialContainerCodeGenerator, NullMaterialContainerCodeGenerator>();
        }

        // Register interceptors
        services.AddScoped<PostgresExceptionLoggingInterceptor>();
        services.AddScoped<NpgsqlConnectionInterceptor>();

        // Register exception translator (used by GridLayoutRepository to surface domain exceptions
        // and log SqlState/Operation at the Persistence boundary — distinct from the SaveChanges
        // interceptor which has no operation context and does not fire on read paths).
        services.AddScoped<PostgresExceptionTranslator>();

        // Resilience pipeline + metrics — singleton so the Polly pipeline is built once.
        services.Configure<DbResilienceOptions>(configuration.GetSection(DbResilienceOptions.SectionName));
        services.AddSingleton<DbResilienceMetrics>();
        services.AddSingleton<IDbResiliencePipelineProvider, DbResiliencePipelineProvider>();

        // Register DbContext
        services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            if (useInMemory || connectionString == "InMemory")
            {
                // For testing scenarios where no real database is needed
                options.UseInMemoryDatabase("TestDatabase");
            }
            else
            {
                options.UseNpgsql(dataSource!, npgsql =>
                {
                    npgsql.ExecutionStrategy(deps =>
                        new PollyExecutionStrategy(
                            deps,
                            sp.GetRequiredService<IDbResiliencePipelineProvider>(),
                            sp.GetRequiredService<DbResilienceMetrics>(),
                            sp.GetRequiredService<ILogger<PollyExecutionStrategy>>()));
                });
                options.AddInterceptors(
                    sp.GetRequiredService<PostgresExceptionLoggingInterceptor>(),
                    sp.GetRequiredService<NpgsqlConnectionInterceptor>());
            }
        });

        // Register DbContextFactory for services that need isolated DbContext instances
        // (e.g., SmartsuppWebhookAuditWriter to prevent failed transactions from blocking subsequent operations)
        services.AddDbContextFactory<ApplicationDbContext>((sp, options) =>
        {
            if (useInMemory || connectionString == "InMemory")
            {
                options.UseInMemoryDatabase("TestDatabase");
            }
            else
            {
                options.UseNpgsql(dataSource!, npgsql =>
                {
                    npgsql.ExecutionStrategy(deps =>
                        new PollyExecutionStrategy(
                            deps,
                            sp.GetRequiredService<IDbResiliencePipelineProvider>(),
                            sp.GetRequiredService<DbResilienceMetrics>(),
                            sp.GetRequiredService<ILogger<PollyExecutionStrategy>>()));
                });
                options.AddInterceptors(
                    sp.GetRequiredService<PostgresExceptionLoggingInterceptor>(),
                    sp.GetRequiredService<NpgsqlConnectionInterceptor>());
            }
        }, lifetime: ServiceLifetime.Scoped);

        // Register telemetry services
        services.AddScoped<ITelemetryService, NoOpTelemetryService>(); // Default to NoOp, can be overridden by API layer

        // NOTE: Repository bindings live in each module's {Feature}Module.cs (vertical-slice
        // composition), not here. PersistenceModule owns only shared infrastructure: the
        // DbContext, NpgsqlDataSource, interceptors, telemetry, and the code generator.
        // See docs/architecture/development_guidelines.md (§Dependency Injection Patterns).

        return services;
    }
}