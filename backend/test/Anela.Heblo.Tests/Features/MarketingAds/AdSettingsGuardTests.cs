using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using FluentAssertions;

namespace Anela.Heblo.Tests.Features.MarketingAds;

public class AdSettingsGuardTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-- stored in Key Vault --")]
    [InlineData("-- stored in secrets.json --")]
    [InlineData("  -- stored in Key Vault --")]
    [InlineData("act_XXXXXXXXX")]
    [InlineData("XXX-XXX-XXXX")]
    [InlineData("your-entra-id-group-id-here")]
    [InlineData("Your-Api-Token")]
    public void IsConfigured_returns_false_for_blank_values_and_placeholders(string? value)
    {
        // Act
        var isConfigured = AdSettingsGuard.IsConfigured(value);

        // Assert
        isConfigured.Should().BeFalse();
    }

    [Theory]
    [InlineData("act_123456789")]
    [InlineData("123-456-7890")]
    [InlineData("Host=heblosql.postgres.database.azure.com;Database=Heblo_V3;Username=heblo")]
    [InlineData("EAAB-real-looking-token")]
    public void IsConfigured_returns_true_for_real_values(string value)
    {
        AdSettingsGuard.IsConfigured(value).Should().BeTrue();
    }

    [Fact]
    public void IsConfigured_returns_false_when_any_of_several_values_is_a_placeholder()
    {
        AdSettingsGuard.IsConfigured("123-456-7890", "-- stored in Key Vault --", "token").Should().BeFalse();
    }

    [Fact]
    public void IsConfigured_returns_true_when_every_value_is_real()
    {
        AdSettingsGuard.IsConfigured("123-456-7890", "developer-token", "refresh-token").Should().BeTrue();
    }

    [Fact]
    public void IsConfigured_returns_false_when_called_without_values()
    {
        AdSettingsGuard.IsConfigured().Should().BeFalse();
        AdSettingsGuard.IsConfigured(null!).Should().BeFalse();
    }
}
