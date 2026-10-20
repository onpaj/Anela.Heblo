using Anela.Heblo.Adapters.GoogleAds;
using Anela.Heblo.Application.Features.MarketingAds;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Infrastructure.Resilience;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Anela.Heblo.AdsBackfill;

/// <summary>The tool's container, callable from a test so the DI chain behind AdsBackfillService stays resolvable.</summary>
public static class AdsBackfillServices
{
    public static IServiceCollection AddAdsBackfill(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddLogging(b => b
            .AddSimpleConsole(o => o.TimestampFormat = "HH:mm:ss ")
            .SetMinimumLevel(LogLevel.Information)
            // EF logs every statement at Information; over a year of search terms that is all you would see.
            .AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning)
            .AddFilter("System.Net.Http.HttpClient", LogLevel.Warning));

        // The resilience plumbing AddAdsPersistenceServices expects from the API host.
        services.AddSingleton<System.Diagnostics.Metrics.IMeterFactory, DefaultMeterFactory>();
        services.AddSingleton<DbResilienceMetrics>();
        services.AddSingleton<NpgsqlConnectionInterceptor>();

        // Both registrations stay silent (stderr warning only) when their settings are missing or are
        // placeholders, exactly as in the API; Program turns that into a clear exit.
        services.AddMarketingAdsModule(configuration);
        services.AddGoogleAdsMarketingAds(configuration);
        AddHebloDbContext(services, configuration);
        return services;
    }

    /// <summary>
    /// The change sync classifies each platform change against Heblo's own executions (C3's AdExecutionLookup →
    /// IAdProposalRepository → ApplicationDbContext). The proposal tables live in the same Heblo database as the
    /// ads schema (AdsDatabase--ConnectionString is a copy of the Default connection string), so the context is
    /// registered on that same connection string, with the vector type mapping the API's PersistenceModule adds.
    /// Built lazily: an unusable string never throws here, and Program exits before anything resolves it.
    /// </summary>
    private static void AddHebloDbContext(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration[MarketingAdsModule.ConnectionStringKey];
        if (!AdSettingsGuard.IsConfigured(connectionString))
            return;

        services.AddSingleton(_ =>
        {
            var builder = new NpgsqlDataSourceBuilder(connectionString);
            builder.UseVector();
            return builder.Build();
        });
        services.AddDbContext<ApplicationDbContext>((sp, options) => options.UseNpgsql(sp.GetRequiredService<NpgsqlDataSource>()));
    }
}

/// <summary>
/// DbResilienceMetrics needs an IMeterFactory; the generic host would normally supply one, and this
/// tool does not build a host.
/// </summary>
internal sealed class DefaultMeterFactory : System.Diagnostics.Metrics.IMeterFactory
{
    public System.Diagnostics.Metrics.Meter Create(System.Diagnostics.Metrics.MeterOptions options) =>
        new(options.Name, options.Version);

    public void Dispose() { }
}
