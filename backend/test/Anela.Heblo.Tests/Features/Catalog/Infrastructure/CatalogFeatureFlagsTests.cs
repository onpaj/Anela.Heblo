using Anela.Heblo.Application.Features.FeatureFlags;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure;

public class CatalogFeatureFlagsTests
{
    [Fact]
    public void FeatureFlagRegistry_ContainsCatalogFlags()
    {
        var keys = FeatureFlagRegistry.All.Select(d => d.Key).ToList();

        keys.Should().Contain(FeatureFlagKeys.TransportBoxTracking);
        keys.Should().Contain(FeatureFlagKeys.StockTaking);
        keys.Should().Contain(FeatureFlagKeys.BackgroundRefresh);
    }

    [Theory]
    [InlineData(FeatureFlagKeys.TransportBoxTracking, false)]
    [InlineData(FeatureFlagKeys.StockTaking, false)]
    [InlineData(FeatureFlagKeys.BackgroundRefresh, true)]
    public void FeatureFlagRegistry_CatalogFlagsHaveCorrectDefaults(string key, bool expectedDefault)
    {
        var def = FeatureFlagRegistry.All.Single(d => d.Key == key);
        def.DefaultValue.Should().Be(expectedDefault);
    }

    [Fact]
    public void FeatureFlagRegistry_CatalogFlags_HaveNonEmptyDescriptions()
    {
        var catalogKeys = new[]
        {
            FeatureFlagKeys.TransportBoxTracking,
            FeatureFlagKeys.StockTaking,
            FeatureFlagKeys.BackgroundRefresh,
        };

        foreach (var key in catalogKeys)
        {
            var def = FeatureFlagRegistry.All.SingleOrDefault(d => d.Key == key);
            def.Should().NotBeNull(because: $"flag '{key}' must be in the registry");
            def!.Description.Should().NotBeNullOrWhiteSpace(because: $"flag '{key}' must have a description");
        }
    }
}
