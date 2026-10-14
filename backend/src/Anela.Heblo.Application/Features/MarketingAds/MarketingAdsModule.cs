using System.Globalization;
using Anela.Heblo.Application.Features.MarketingAds.Configuration;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Reporting;
using Anela.Heblo.Application.Features.MarketingAds.Sync;
using Anela.Heblo.Persistence.Ads;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Anela.Heblo.Application.Features.MarketingAds;

/// <summary>
/// Composition root of the MarketingAds module (spec docs/superpowers/specs/2026-10-07-marketing-agents-platform-design.md).
/// PR C1 registers the ads schema; C2 registers the ads sync (daily + change detection) and its jobs; the proposal layer (C3) adds its own here.
/// </summary>
public static class MarketingAdsModule
{
    public const string ConnectionStringKey = "AdsDatabase:ConnectionString";
    public const string MaxPoolSizeKey = "AdsDatabase:MaxPoolSize";
    private const int DefaultMaxPoolSize = 5;

    public static IServiceCollection AddMarketingAdsModule(this IServiceCollection services, IConfiguration configuration)
    {
        AddAdsPersistenceWhenConfigured(services, configuration);
        AddAdsSync(services, configuration);
        return services;
    }

    private static void AddAdsSync(IServiceCollection services, IConfiguration configuration)
    {
        // No ValidateOnStart: invalid Ads settings must fail a job run, never app startup (AdsSyncOptions.EnsureValid).
        services.Configure<AdsSyncOptions>(configuration.GetSection(AdsSyncOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);

        // Fallback until C3: nothing is executed by Heblo yet. C3 replaces this registration.
        services.AddScoped<IAdExecutionLookup, NoOpAdExecutionLookup>();
        services.AddSingleton<AdChangeOriginMatcher>();

        // Everything below needs AdsDbContext, which is registered only when AdsDatabase:ConnectionString
        // is configured. Factories keep ValidateOnBuild (on in Development) from failing when it is absent;
        // nothing resolves them unless the context exists. All Scoped: read sources are Scoped.
        AddAdsScoped<IAdSyncStateStore, AdSyncStateStore>(services);
        AddAdsScoped<AdAccountUpserter>(services);
        AddAdsScoped<AdEntityUpserter>(services);
        AddAdsScoped<AdDailyFactUpserter>(services);
        AddAdsScoped<AdSearchTermUpserter>(services);
        AddAdsScoped<AdChangeEventWriter>(services);
        AddAdsScoped<AdStreamRunner>(services);
        AddAdsScoped<AdEntitySyncStep>(services);

        // Resolved only by backend/tools/Anela.Heblo.AdsBackfill, never by a job.
        AddAdsScoped<AdsBackfillService>(services);

        // The jobs (auto-discovered by AddRecurringJobs) depend only on these two, which always resolve.
        services.AddScoped<IAdsDailySyncService>(sp => sp.GetService<AdsDbContext>() is null
            ? new DisabledAdsSyncService()
            : ActivatorUtilities.CreateInstance<AdsDailySyncService>(sp));
        services.AddScoped<IAdsChangeSyncService>(sp => sp.GetService<AdsDbContext>() is null
            ? new DisabledAdsSyncService()
            : ActivatorUtilities.CreateInstance<AdsChangeSyncService>(sp));

        // Read side for the agents' MCP tools; Unavailable when the ads database is not configured.
        services.AddScoped<IAdsReadRepository>(sp => sp.GetService<AdsDbContext>() is { } db
            ? new AdsReadRepository(db)
            : new UnavailableAdsReadRepository());
    }

    private static void AddAdsScoped<TService, TImplementation>(IServiceCollection services)
        where TService : class
        where TImplementation : class, TService =>
        services.AddScoped<TService>(sp => ActivatorUtilities.CreateInstance<TImplementation>(sp));

    private static void AddAdsScoped<TService>(IServiceCollection services)
        where TService : class =>
        services.AddScoped(sp => ActivatorUtilities.CreateInstance<TService>(sp));

    /// <summary>
    /// The ads schema stays unregistered — and the environment inert — unless the connection string
    /// is real. A Key Vault placeholder or a typo'd secret resolving to prose clears a blank check and
    /// then throws inside NpgsqlDataSourceBuilder during registration, which would take the whole API
    /// down at boot (same reasoning as ShoptetOrdersAnalyticsServiceCollectionExtensions).
    /// </summary>
    private static void AddAdsPersistenceWhenConfigured(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration[ConnectionStringKey];
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        if (!AdSettingsGuard.IsConfigured(connectionString))
        {
            LogStartupWarning(
                $"{ConnectionStringKey} looks like a placeholder; the ads schema stays unregistered. "
                + "Check the Key Vault secret AdsDatabase--ConnectionString.");
            return;
        }

        if (!IsParseableConnectionString(connectionString!, out var parseError))
        {
            LogStartupWarning(
                $"{ConnectionStringKey} is set but is not a valid Npgsql connection string ({parseError}); "
                + "the ads schema stays unregistered. Check the Key Vault secret AdsDatabase--ConnectionString.");
            return;
        }

        if (!TryReadMaxPoolSize(configuration, out var maxPoolSize))
        {
            LogStartupWarning(
                $"{MaxPoolSizeKey} must be a positive integer (got '{configuration[MaxPoolSizeKey]}'); "
                + "the ads schema stays unregistered.");
            return;
        }

        try
        {
            services.AddAdsPersistenceServices(connectionString!, maxPoolSize);
        }
        catch (Exception ex)
        {
            // The data source is built before anything is added to the collection, so a failure here
            // leaves nothing half-registered.
            LogStartupWarning(
                $"building the ads data source failed ({ex.GetType().Name}: {ex.Message}); "
                + "the ads schema stays unregistered.");
        }
    }

    private static bool TryReadMaxPoolSize(IConfiguration configuration, out int maxPoolSize)
    {
        var raw = configuration[MaxPoolSizeKey];
        if (string.IsNullOrWhiteSpace(raw))
        {
            maxPoolSize = DefaultMaxPoolSize;
            return true;
        }

        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out maxPoolSize) && maxPoolSize > 0;
    }

    private static bool IsParseableConnectionString(string value, out string error)
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(value);
            if (string.IsNullOrWhiteSpace(builder.Host))
            {
                error = "no Host";
                return false;
            }

            error = "";
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>Registration runs before the logging pipeline exists, so the warning goes straight to stderr.</summary>
    private static void LogStartupWarning(string message) =>
        Console.Error.WriteLine($"[MarketingAds] {message}");
}
