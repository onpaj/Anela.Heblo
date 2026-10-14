using Anela.Heblo.Adapters.GoogleAds.Execution;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.GoogleAds.Tests.Execution;

/// <summary>
/// Live, validate-only check against the real account. Category=Integration keeps it out of CI; the
/// SmokeFact skips it unless HEBLO_GOOGLE_ADS_SMOKE_ENV is set. Nothing is changed in the account.
/// </summary>
[Trait("Category", "Integration")]
public sealed class GoogleAdsValidateOnlySmokeTests
{
    [SmokeFact]
    public async Task google_accepts_both_actions_in_validate_only_mode_and_nothing_changes()
    {
        var env = SmokeEnvironment.Load();
        using var provider = env.BuildProvider();
        provider.GetRequiredService<IOptionsMonitor<GoogleAdsSettings>>().CurrentValue.ValidateOnly
            .Should().BeTrue("the smoke test must never write");
        using var scope = provider.CreateScope();
        var executor = scope.ServiceProvider.GetRequiredService<IAdActionExecutor>();

        var pause = new AdAction(AdActionType.PauseAd, AdPlatform.GoogleAds, env.CustomerId, AdEntityLevel.Ad,
            env.AdGroupAdId, AdActionValues.Enabled, AdActionValues.Paused, new Dictionary<string, string>());
        var negative = new AdAction(AdActionType.AddNegativeKeyword, AdPlatform.GoogleAds, env.CustomerId,
            AdEntityLevel.Campaign, env.CampaignId, AdActionValues.Absent, AdActionValues.Present,
            new Dictionary<string, string>
            {
                [AdActionPayloadKeys.Text] = "heblo smoke test",
                [AdActionPayloadKeys.MatchType] = nameof(KeywordMatchType.Exact),
            });

        foreach (var action in new[] { pause, negative })
        {
            var before = await executor.ReadCurrentAsync(action, CancellationToken.None);
            before.Exists.Should().BeTrue($"{action.Type} target {action.TargetExternalId} must exist");

            var result = await executor.ExecuteAsync(action, CancellationToken.None);
            result.Error.Should().StartWith(GoogleAdsExecutionResults.ValidateOnlyMarker,
                "Google must accept the {0} request; got: {1}", action.Type, result.Error);

            (await executor.ReadCurrentAsync(action, CancellationToken.None)).CurrentValue
                .Should().Be(before.CurrentValue, "validate-only must not change the account");
        }
    }
}
