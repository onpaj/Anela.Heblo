using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Dtos;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Actions;

/// <summary>Builds C1 AdActions with the spec 12.2 value conventions. Call only after AdProposalShapeValidator passed.</summary>
public static class AdActionFactory
{
    public static AdAction Create(AdPlatform platform, string accountExternalId, AdActionType type, AdActionInputDto input)
    {
        var account = NormalizeAccountId(platform, accountExternalId);
        return type switch
        {
            AdActionType.AddNegativeKeyword => new AdAction(
                type, platform, account, input.TargetLevel, input.TargetExternalId.Trim(),
                AdActionValues.Absent, AdActionValues.Present,
                new Dictionary<string, string>
                {
                    [AdActionPayloadKeys.Text] = input.KeywordText!.Trim(),
                    [AdActionPayloadKeys.MatchType] = input.MatchType!.Value.ToString(),
                }),
            AdActionType.PauseAd => new AdAction(
                type, platform, account, AdEntityLevel.Ad, input.TargetExternalId.Trim(),
                AdActionValues.Enabled, AdActionValues.Paused, new Dictionary<string, string>()),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Action type has no factory rule."),
        };
    }

    /// <summary>The ads schema stores Google Ads customer ids without dashes.</summary>
    public static string NormalizeAccountId(AdPlatform platform, string accountExternalId)
    {
        var trimmed = accountExternalId.Trim();
        return platform == AdPlatform.GoogleAds ? trimmed.Replace("-", string.Empty) : trimmed;
    }
}
