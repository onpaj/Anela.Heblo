using System.Diagnostics.Metrics;
using Anela.Heblo.Adapters.ShoptetApi.Analytics;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Persistence.Infrastructure.Resilience;
using Anela.Heblo.Persistence.ShoptetOrders;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Analytics;

public class ShoptetOrdersAnalyticsRegistrationTests
{
    private static IConfiguration BuildConfiguration(string? connectionString)
    {
        var values = new Dictionary<string, string?>
        {
            ["Shoptet:BaseUrl"] = "https://api.myshoptet.com",
            ["Shoptet:ApiToken"] = "token",
        };

        if (connectionString != null)
            values["ShoptetOrdersSync:ConnectionString"] = connectionString;

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static ServiceCollection BuildServices(string? connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IMeterFactory>(new TestMeterFactory());
        services.AddSingleton<DbResilienceMetrics>();
        services.AddSingleton<NpgsqlConnectionInterceptor>();
        services.AddOptions<Anela.Heblo.Adapters.ShoptetApi.Orders.ShoptetApiSettings>()
            .Bind(BuildConfiguration(connectionString).GetSection("Shoptet"));
        services.AddShoptetOrdersAnalytics(BuildConfiguration(connectionString));
        return services;
    }

    [Fact]
    public void An_environment_with_no_connection_string_registers_nothing()
    {
        // Arrange & Act — the same inert-by-default gate flexi_raw uses.
        var services = BuildServices(connectionString: null);

        // Assert
        services.Should().NotContain(d => d.ServiceType == typeof(IRecurringJob));
        services.Should().NotContain(d => d.ServiceType == typeof(ShoptetOrdersDbContext));
    }

    [Fact]
    public void An_empty_connection_string_registers_nothing()
    {
        var services = BuildServices(connectionString: "   ");

        services.Should().NotContain(d => d.ServiceType == typeof(IRecurringJob));
    }

    [Fact]
    public void A_configured_environment_registers_the_job_and_the_whole_sync_graph()
    {
        // Arrange
        var services = BuildServices("Host=localhost;Database=Heblo_V3;Username=test;Password=test");

        // Act
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        // Assert — both the interface (for discovery) and the concrete type (for Hangfire's
        // activator, which resolves the job type when the schedule fires).
        scope.ServiceProvider.GetServices<IRecurringJob>()
            .Should().ContainSingle().Which.Should().BeOfType<ShoptetOrdersSyncJob>();
        scope.ServiceProvider.GetService<ShoptetOrdersSyncJob>().Should().NotBeNull();
        scope.ServiceProvider.GetService<IShoptetOrdersSyncService>().Should().NotBeNull();
        scope.ServiceProvider.GetService<ShoptetOrderBackfillService>().Should().NotBeNull();
        scope.ServiceProvider.GetService<ShoptetOrderIncrementalSyncService>().Should().NotBeNull();
        scope.ServiceProvider.GetService<IShoptetOrderStore>().Should().NotBeNull();
        scope.ServiceProvider.GetService<ShoptetOrdersDbContext>().Should().NotBeNull();
    }

    [Fact]
    public void The_shoptet_orders_resilience_pipeline_is_isolated_from_the_request_serving_one()
    {
        // Arrange — the sync SaveChangesAsync's whole batches and must not inherit the tight
        // per-request budget, the same reasoning as AnalyticsPersistenceModule for flexi_raw.
        var services = BuildServices("Host=localhost;Database=Heblo_V3;Username=test;Password=test");
        services.AddSingleton<IDbResiliencePipelineProvider>(sp =>
            new DbResiliencePipelineProvider(
                Microsoft.Extensions.Options.Options.Create(new DbResilienceOptions()),
                sp.GetRequiredService<DbResilienceMetrics>(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<DbResiliencePipelineProvider>.Instance));

        // Act
        using var provider = services.BuildServiceProvider();

        // Assert
        provider.GetRequiredKeyedService<IDbResiliencePipelineProvider>("shoptet-orders")
            .Should().NotBeSameAs(provider.GetRequiredService<IDbResiliencePipelineProvider>());
    }

    private sealed class TestMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options.Name, options.Version);
        public void Dispose() { }
    }
}
