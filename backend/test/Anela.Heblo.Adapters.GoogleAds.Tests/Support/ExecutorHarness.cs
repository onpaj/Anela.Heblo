using Anela.Heblo.Adapters.GoogleAds;
using Anela.Heblo.Adapters.GoogleAds.Execution;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace Anela.Heblo.Adapters.GoogleAds.Tests.Support;

internal static class ExecutorHarness
{
    /// <summary>Campaign 111 with ad group 221 holding the enabled ad 221~441; no negatives yet.</summary>
    public static StatefulFakeGoogleAdsApi SeededApi()
    {
        var api = new StatefulFakeGoogleAdsApi();
        api.Campaigns.Add("111");
        api.AdGroups.Add("221");
        api.AdStatuses["221~441"] = "ENABLED";
        return api;
    }

    public static GoogleAdsActionExecutor Create(StatefulFakeGoogleAdsApi api, bool validateOnly = false) =>
        new(api,
            new TestOptionsMonitor<GoogleAdsSettings>(TestSettings.Create(s => s.ValidateOnly = validateOnly)),
            NullLogger<GoogleAdsActionExecutor>.Instance);

    public static AdAction PauseAd(string target, string? account = null) => new(
        AdActionType.PauseAd, AdPlatform.GoogleAds, account ?? TestSettings.CustomerId,
        AdEntityLevel.Ad, target, AdActionValues.Enabled, AdActionValues.Paused,
        new Dictionary<string, string>());

    public static AdAction AddNegative(AdEntityLevel level, string target, string text, string matchType) => new(
        AdActionType.AddNegativeKeyword, AdPlatform.GoogleAds, TestSettings.CustomerId,
        level, target, AdActionValues.Absent, AdActionValues.Present,
        new Dictionary<string, string> { [AdActionPayloadKeys.Text] = text, [AdActionPayloadKeys.MatchType] = matchType });
}
