using Anela.Heblo.Adapters.GoogleAnalytics.Sync;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Persistence.Ga4;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Anela.Heblo.Adapters.GoogleAnalytics;

public static class GoogleAnalyticsAdapterServiceCollectionExtensions
{
    /// <summary>
    /// Registers the GA4 aggregate ingestion, but only when the property id and credentials are
    /// both configured — an unconfigured environment stays completely inert, the same way the
    /// Flexi analytics stack gates on its connection string.
    ///
    /// The ga4_agg schema lives inside the main Heblo database (ADR-007), so the DbContext uses
    /// the same connection string the rest of the app does. No separate database, and no extra
    /// Key Vault secret for one.
    /// </summary>
    public static IServiceCollection AddGoogleAnalyticsAdapter(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var options = new Ga4Options();
        configuration.GetSection(Ga4Options.ConfigurationKey).Bind(options);

        if (!options.IsConfigured)
            return services;

        var connectionString = configuration.GetConnectionString(environment.EnvironmentName);
        if (string.IsNullOrWhiteSpace(connectionString) || connectionString == "InMemory")
            return services;

        services.Configure<Ga4Options>(configuration.GetSection(Ga4Options.ConfigurationKey));
        services.Configure<Ga4SyncOptions>(configuration.GetSection(Ga4SyncOptions.ConfigurationKey));

        services.AddGa4PersistenceServices(
            connectionString,
            configuration.GetValue<int?>("Ga4Database:MaxPoolSize") ?? 5);

        services.AddSingleton<IGa4ReportClient, Ga4ReportClient>();
        services.AddScoped<IGa4SyncWatermarkRepository, Ga4SyncWatermarkRepository>();

        // Order matters only for log readability; each service owns its own watermark.
        services.AddScoped<IGa4EntitySyncService, TrafficMonthlySyncService>();
        services.AddScoped<IGa4EntitySyncService, TrafficTotalSyncService>();
        services.AddScoped<IGa4EntitySyncService, TrafficSyncService>();
        services.AddScoped<IGa4EntitySyncService, ConversionsSyncService>();
        services.AddScoped<IGa4EntitySyncService, LandingPageSyncService>();
        services.AddScoped<IGa4EntitySyncService, PageSyncService>();

        services.AddScoped<IGa4SyncService, Ga4SyncService>();

        // Both registrations are required. IRecurringJob is what RecurringJobDiscoveryService
        // enumerates to find the job and its cron; the concrete type is what Hangfire resolves,
        // because RecurringJob.AddOrUpdate<TJob> stores TJob and AspNetCoreJobActivator then does
        // GetRequiredService(typeof(Ga4SyncJob)) at execution time. Register only the interface
        // and the job appears in Hangfire correctly and then throws the first time it fires.
        services.AddScoped<IRecurringJob, Ga4SyncJob>();
        services.AddScoped<Ga4SyncJob>();

        return services;
    }
}
