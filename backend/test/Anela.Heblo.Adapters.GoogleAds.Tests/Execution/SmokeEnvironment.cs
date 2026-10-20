using Anela.Heblo.Adapters.GoogleAds.Tests.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Anela.Heblo.Adapters.GoogleAds.Tests.Execution;

/// <summary>Builds the real DI graph from the spike's env file, with ValidateOnly forced on.</summary>
internal sealed record SmokeEnvironment(string CustomerId, string AdGroupAdId, string CampaignId, IReadOnlyDictionary<string, string?> Configuration)
{
    public const string AdVariable = "HEBLO_GOOGLE_ADS_SMOKE_AD";
    public const string CampaignVariable = "HEBLO_GOOGLE_ADS_SMOKE_CAMPAIGN";

    public static SmokeEnvironment Load()
    {
        var env = File.ReadAllLines(Environment.GetEnvironmentVariable(SmokeFactAttribute.EnvFileVariable)!)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#') && line.Contains('='))
            .Select(line => line.Split('=', 2))
            .GroupBy(parts => parts[0].Trim())
            .ToDictionary(group => group.Key, group => group.Last()[1].Trim().Trim('"', '\''));

        var customerId = env["GOOGLE_ADS_CUSTOMER_ID"].Replace("-", string.Empty);
        var configuration = new Dictionary<string, string?>
        {
            ["GoogleAds:CustomerId"] = customerId,
            ["GoogleAds:LoginCustomerId"] = env.GetValueOrDefault("GOOGLE_ADS_LOGIN_CUSTOMER_ID", string.Empty),
            ["GoogleAds:OAuth2ClientId"] = env["GOOGLE_ADS_OAUTH_CLIENT_ID"],
            ["GoogleAds:OAuth2ClientSecret"] = env["GOOGLE_ADS_OAUTH_CLIENT_SECRET"],
            ["GoogleAds:OAuth2RefreshToken"] = env["GOOGLE_ADS_REFRESH_TOKEN"],
            ["GoogleAds:ValidateOnly"] = "true",
        };
        return new SmokeEnvironment(
            customerId,
            Environment.GetEnvironmentVariable(AdVariable) ?? throw new InvalidOperationException($"Set {AdVariable}=<adGroupId>~<adId>."),
            Environment.GetEnvironmentVariable(CampaignVariable) ?? throw new InvalidOperationException($"Set {CampaignVariable}=<campaignId>."),
            configuration);
    }

    public ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<IHostEnvironment>(new StubHostEnvironment("Staging"));
        services.AddGoogleAdsMarketingAds(new ConfigurationBuilder().AddInMemoryCollection(Configuration).Build());
        return services.BuildServiceProvider(validateScopes: true);
    }
}
