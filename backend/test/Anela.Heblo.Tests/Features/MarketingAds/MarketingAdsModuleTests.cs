using Anela.Heblo.Application.Features.MarketingAds;
using Anela.Heblo.Persistence.Ads;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Tests.Features.MarketingAds;

public class MarketingAdsModuleTests
{
    private static IServiceCollection Register(string? connectionString)
    {
        var settings = new Dictionary<string, string?>();
        if (connectionString is not null)
            settings[MarketingAdsModule.ConnectionStringKey] = connectionString;

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

    [Fact]
    public void AddMarketingAdsModule_registers_AdsDbContext_when_a_real_connection_string_is_configured()
    {
        var services = Register("Host=localhost;Database=Heblo_V3;Username=heblo");

        services.Should().Contain(d => d.ServiceType == typeof(AdsDbContext));
    }
}
