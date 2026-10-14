using Anela.Heblo.Adapters.GoogleAds.Api;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Adapters.GoogleAds.Execution;

internal static class GoogleAdsActionGuard
{
    /// <returns>null when the action may be sent to Google, otherwise why not.</returns>
    public static string? Validate(AdAction action, string configuredCustomerId) =>
        action.Platform != AdPlatform.GoogleAds
            ? $"Action is for {action.Platform}, not GoogleAds."
            : GoogleAdsIds.NormalizeCustomerId(action.AccountExternalId) != configuredCustomerId
                ? $"Account '{action.AccountExternalId}' is not the configured Google Ads account."
                : null;
}
