using System.Globalization;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Persistence.Ads;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Anela.Heblo.Application.Features.MarketingAds;

/// <summary>
/// Composition root of the MarketingAds module (spec docs/superpowers/specs/2026-10-07-marketing-agents-platform-design.md).
/// PR C1 registers only the ads schema; the sync jobs (C2) and the proposal layer (C3) add theirs here.
/// </summary>
public static class MarketingAdsModule
{
    public const string ConnectionStringKey = "AdsDatabase:ConnectionString";
    public const string MaxPoolSizeKey = "AdsDatabase:MaxPoolSize";
    private const int DefaultMaxPoolSize = 5;

    public static IServiceCollection AddMarketingAdsModule(this IServiceCollection services, IConfiguration configuration)
    {
        AddAdsPersistenceWhenConfigured(services, configuration);
        return services;
    }

    /// <summary>
    /// The ads schema stays unregistered — and the environment inert — unless the connection string
    /// is real. A Key Vault placeholder or a typo'd secret resolving to prose clears a blank check and
    /// then throws inside NpgsqlDataSourceBuilder during registration, which would take the whole API
    /// down at boot (same reasoning as ShoptetOrdersAnalyticsServiceCollectionExtensions).
    /// </summary>
    private static void AddAdsPersistenceWhenConfigured(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration[ConnectionStringKey];
        if (!AdSettingsGuard.IsConfigured(connectionString))
            return;

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
