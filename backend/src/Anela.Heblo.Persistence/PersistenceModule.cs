using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.Bank;
using Anela.Heblo.Domain.Features.Campaigns;
using Anela.Heblo.Domain.Features.GridLayouts;
using Anela.Heblo.Persistence.Campaigns;
using Anela.Heblo.Persistence.GridLayouts;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.InvoiceClassification;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using Anela.Heblo.Persistence.BackgroundJobs;
using Anela.Heblo.Persistence.Catalog.Stock;
using Anela.Heblo.Persistence.Dashboard;
using Anela.Heblo.Persistence.Features.Bank;
using Anela.Heblo.Persistence.Infrastructure;
using Anela.Heblo.Persistence.InvoiceClassification;
using Anela.Heblo.Persistence.KnowledgeBase;
using Anela.Heblo.Xcc.Services.Dashboard;
using Anela.Heblo.Xcc.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

        // Register interceptors
        services.AddScoped<PostgresExceptionLoggingInterceptor>();

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
                options.UseNpgsql(dataSource!);
                options.AddInterceptors(sp.GetRequiredService<PostgresExceptionLoggingInterceptor>());
            }
        });

        // Register telemetry services
        services.AddScoped<ITelemetryService, NoOpTelemetryService>(); // Default to NoOp, can be overridden by API layer

        // Register repositories
        services.AddScoped<IUserDashboardSettingsRepository, UserDashboardSettingsRepository>();

        // Bank repositories
        services.AddScoped<IBankStatementImportRepository, BankStatementImportRepository>();

        // Invoice Classification repositories
        services.AddScoped<IClassificationRuleRepository, ClassificationRuleRepository>();
        services.AddScoped<IClassificationHistoryRepository, ClassificationHistoryRepository>();

        // Stock repositories
        services.AddScoped<IStockUpOperationRepository, StockUpOperationRepository>();

        // Background Jobs repositories
        services.AddScoped<IRecurringJobConfigurationRepository, RecurringJobConfigurationRepository>();

        // KnowledgeBase repositories
        services.AddScoped<IKnowledgeBaseRepository, KnowledgeBaseRepository>();

        // Grid Layouts repositories
        services.AddScoped<IGridLayoutRepository, GridLayoutRepository>();

        // Campaigns repositories
        services.AddScoped<ICampaignRepository, CampaignRepository>();

        return services;
    }
}