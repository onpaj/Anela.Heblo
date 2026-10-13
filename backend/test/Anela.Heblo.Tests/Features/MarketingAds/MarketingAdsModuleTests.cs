using Anela.Heblo.Application.Features.MarketingAds;
using Anela.Heblo.Persistence.Ads;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Tests.Features.MarketingAds;

public class MarketingAdsModuleTests
{
    private const string RealConnectionString = "Host=localhost;Database=Heblo_V3;Username=heblo";

    private static IServiceCollection Register(string? connectionString, string? maxPoolSize = null)
    {
        var settings = new Dictionary<string, string?>();
        if (connectionString is not null)
            settings[MarketingAdsModule.ConnectionStringKey] = connectionString;
        if (maxPoolSize is not null)
            settings[MarketingAdsModule.MaxPoolSizeKey] = maxPoolSize;

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new ServiceCollection().AddMarketingAdsModule(configuration);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("-- stored in Key Vault --")]
    [InlineData("not a connection string")]
    [InlineData("InMemory")]
    [InlineData("Database=Heblo_V3;Username=heblo")]
    public void AddMarketingAdsModule_leaves_the_ads_schema_unregistered_when_the_connection_string_is_not_usable(string? connectionString)
    {
        // Act
        var services = Register(connectionString);

        // Assert — and, implicitly, registration did not throw (a throw here would stop the API booting)
        services.Should().NotContain(d => d.ServiceType == typeof(AdsDbContext));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("2.5")]
    public void AddMarketingAdsModule_leaves_the_ads_schema_unregistered_when_the_pool_size_is_not_a_positive_integer(string maxPoolSize)
    {
        var services = Register(RealConnectionString, maxPoolSize);

        services.Should().NotContain(d => d.ServiceType == typeof(AdsDbContext));
    }

    [Theory]
    [InlineData("Host=localhost;Port=notanumber")]
    [InlineData("Host=localhost;Username=heblo;Pooling=maybe")]
    public void AddMarketingAdsModule_leaves_the_ads_schema_unregistered_when_the_data_source_cannot_be_built(string connectionString)
    {
        var services = Register(connectionString);

        services.Should().NotContain(d => d.ServiceType == typeof(AdsDbContext));
    }

    [Fact]
    public void AddMarketingAdsModule_registers_AdsDbContext_with_an_explicit_valid_pool_size()
    {
        var services = Register(RealConnectionString, "10");

        services.Should().Contain(d => d.ServiceType == typeof(AdsDbContext));
    }

    [Fact]
    public void AddMarketingAdsModule_registers_AdsDbContext_when_a_real_connection_string_is_configured()
    {
        var services = Register(RealConnectionString);

        services.Should().Contain(d => d.ServiceType == typeof(AdsDbContext));
    }
}
