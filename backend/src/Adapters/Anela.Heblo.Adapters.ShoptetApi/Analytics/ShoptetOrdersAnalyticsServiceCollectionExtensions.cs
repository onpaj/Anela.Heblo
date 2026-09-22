using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Persistence.ShoptetOrders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.ShoptetApi.Analytics;

public static class ShoptetOrdersAnalyticsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the shoptet_raw mirror. Like the flexi_raw stack, the whole thing stays
    /// unregistered when no connection string is configured, so an unconfigured environment is inert.
    /// </summary>
    public static IServiceCollection AddShoptetOrdersAnalytics(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration[$"{ShoptetOrdersSyncOptions.ConfigurationKey}:ConnectionString"];
        if (string.IsNullOrWhiteSpace(connectionString))
            return services;

        services.Configure<ShoptetOrdersSyncOptions>(
            configuration.GetSection(ShoptetOrdersSyncOptions.ConfigurationKey));

        var maxPoolSize = configuration.GetValue<int?>(
            $"{ShoptetOrdersSyncOptions.ConfigurationKey}:MaxPoolSize") ?? 10;

        services.AddShoptetOrdersPersistenceServices(connectionString, maxPoolSize);

        var requestsPerSecond = configuration.GetValue<double?>(
            $"{ShoptetOrdersSyncOptions.ConfigurationKey}:RequestsPerSecond") ?? 3.0;

        // Singleton: the throttle is the process-wide pacer for the Shoptet API, so every call the
        // sync makes has to queue behind the same clock.
        services.AddSingleton(new ShoptetApiThrottle(requestsPerSecond));
        services.TryAddTimeProvider();

        services.AddHttpClient<IShoptetOrderAnalyticsClient, ShoptetOrderAnalyticsClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<IOptions<ShoptetApiSettings>>().Value;
            var options = sp.GetRequiredService<IOptions<ShoptetOrdersSyncOptions>>().Value;
            client.BaseAddress = new Uri(settings.BaseUrl);
            client.DefaultRequestHeaders.Add("Shoptet-Private-API-Token", settings.ApiToken);
            client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
        });

        services.AddScoped<IShoptetOrderStore, ShoptetOrderStore>();
        services.AddScoped<IShoptetSyncWatermarkRepository, ShoptetSyncWatermarkRepository>();
        services.AddScoped<ShoptetOrderIngestor>();
        services.AddScoped<ShoptetOrderBackfillService>();
        services.AddScoped<ShoptetOrderIncrementalSyncService>();
        services.AddScoped<IShoptetEntitySyncService>(sp => sp.GetRequiredService<ShoptetOrderBackfillService>());
        services.AddScoped<IShoptetEntitySyncService>(sp => sp.GetRequiredService<ShoptetOrderIncrementalSyncService>());
        services.AddScoped<IShoptetOrdersSyncService, ShoptetOrdersSyncService>();
        // Both registrations are needed: the discovery service resolves IRecurringJob to read the
        // metadata, and Hangfire's activator resolves the concrete type when the job actually runs.
        services.AddScoped<ShoptetOrdersSyncJob>();
        services.AddScoped<IRecurringJob, ShoptetOrdersSyncJob>();

        return services;
    }

    private static IServiceCollection TryAddTimeProvider(this IServiceCollection services)
    {
        if (services.All(d => d.ServiceType != typeof(TimeProvider)))
            services.AddSingleton(TimeProvider.System);

        return services;
    }
}
