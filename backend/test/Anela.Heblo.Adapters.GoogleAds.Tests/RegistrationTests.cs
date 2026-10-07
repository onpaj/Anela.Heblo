using Anela.Heblo.Adapters.GoogleAds;
using Anela.Heblo.Adapters.GoogleAds.Reporting;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Adapters.GoogleAds.Tests;

public sealed class RegistrationTests
{
    private static Dictionary<string, string?> Configured() => new()
    {
        ["GoogleAds:CustomerId"] = "123-456-7890",
        ["GoogleAds:OAuth2ClientId"] = "client-id.apps.googleusercontent.com",
        ["GoogleAds:OAuth2ClientSecret"] = "client-secret-value",
        ["GoogleAds:OAuth2RefreshToken"] = "refresh-token-value",
    };

    private static IServiceCollection Register(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new ServiceCollection().AddGoogleAdsMarketingAds(configuration);
    }

    [Fact]
    public void registers_nothing_with_the_repository_placeholders()
    {
        var settings = new Dictionary<string, string?>
        {
            ["GoogleAds:CustomerId"] = "XXX-XXX-XXXX",
            ["GoogleAds:OAuth2ClientId"] = "-- stored in secrets.json --",
            ["GoogleAds:OAuth2ClientSecret"] = "-- stored in secrets.json --",
            ["GoogleAds:OAuth2RefreshToken"] = "-- stored in secrets.json --",
        };

        Register(settings).Should().BeEmpty("an unconfigured environment must stay inert");
    }

    [Theory]
    [InlineData("GoogleAds:CustomerId")]
    [InlineData("GoogleAds:OAuth2ClientId")]
    [InlineData("GoogleAds:OAuth2ClientSecret")]
    [InlineData("GoogleAds:OAuth2RefreshToken")]
    public void registers_nothing_when_one_credential_is_missing(string key)
    {
        var settings = Configured();
        settings[key] = "";

        Register(settings).Should().BeEmpty();
    }

    [Fact]
    public void registers_nothing_for_a_non_numeric_customer_or_login_id()
    {
        var badCustomer = Configured();
        badCustomer["GoogleAds:CustomerId"] = "anela";
        var badLogin = Configured();
        badLogin["GoogleAds:LoginCustomerId"] = "manager";

        Register(badCustomer).Should().BeEmpty();
        Register(badLogin).Should().BeEmpty();
    }

    [Fact]
    public void reports_no_misconfiguration_when_everything_is_a_placeholder_or_blank()
    {
        var settings = new GoogleAdsSettings
        {
            CustomerId = "XXX-XXX-XXXX",
            OAuth2ClientId = "-- stored in secrets.json --",
        };

        GoogleAdsMarketingAdsServiceCollectionExtensions.DescribeMisconfiguration(settings)
            .Should().BeNull("an environment that never configured Google Ads must not warn at every boot");
    }

    [Fact]
    public void reports_no_misconfiguration_when_fully_configured()
    {
        GoogleAdsMarketingAdsServiceCollectionExtensions.DescribeMisconfiguration(FullyConfigured())
            .Should().BeNull();
    }

    [Fact]
    public void names_the_unusable_settings_without_their_values_when_partly_configured()
    {
        var settings = FullyConfigured();
        settings.OAuth2RefreshToken = "-- stored in Key Vault --";
        settings.LoginCustomerId = "manager";

        var message = GoogleAdsMarketingAdsServiceCollectionExtensions.DescribeMisconfiguration(settings);

        message.Should().Contain("OAuth2RefreshToken").And.Contain("LoginCustomerId")
            .And.NotMatchRegex(@"\bCustomerId\b").And.NotContain("client-secret-value").And.NotContain("manager");
    }

    private static GoogleAdsSettings FullyConfigured() => new()
    {
        CustomerId = "123-456-7890",
        OAuth2ClientId = "client-id.apps.googleusercontent.com",
        OAuth2ClientSecret = "client-secret-value",
        OAuth2RefreshToken = "refresh-token-value",
    };

    [Fact]
    public void does_not_require_a_developer_token()
    {
        Register(Configured()).Should().Contain(d => d.ServiceType == typeof(IAdPlatformReadSource));
    }

    [Fact]
    public void resolves_the_read_source_without_calling_google()
    {
        var services = Register(Configured());
        services.AddLogging();
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        var sources = scope.ServiceProvider.GetServices<IAdPlatformReadSource>().ToList();

        sources.Should().ContainSingle().Which.Should().BeOfType<GoogleAdsReadSource>()
            .Which.Platform.Should().Be(AdPlatform.GoogleAds);
    }
}
