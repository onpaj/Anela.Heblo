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

        var maxPoolSize = configuration.GetValue<int?>(MaxPoolSizeKey) ?? DefaultMaxPoolSize;
        services.AddAdsPersistenceServices(connectionString!, maxPoolSize);
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
