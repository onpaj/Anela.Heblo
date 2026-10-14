using Anela.Heblo.Adapters.GoogleAds.Api;
using Anela.Heblo.Adapters.GoogleAds.Reporting;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Adapters.GoogleAds.Execution;

/// <summary>PauseAd: ad_group_ad status ENABLED → PAUSED; revert sets ENABLED. Target = "{adGroupId}~{adId}".</summary>
internal sealed class GoogleAdsPauseAdHandler
{
    private const string ApiEnabled = "ENABLED";
    private const string ApiPaused = "PAUSED";
    private readonly IGoogleAdsApiClient _api;

    public GoogleAdsPauseAdHandler(IGoogleAdsApiClient api) => _api = api;

    public async Task<AdTargetState> ReadCurrentAsync(string customerId, AdAction action, CancellationToken ct)
    {
        if (!GoogleAdsIds.TryParseComposite(action.TargetExternalId, out var adGroupId, out var adId))
            return GoogleAdsExecutionResults.Missing;

        var rows = await _api.SearchAsync(customerId, GoogleAdsExecutorQueries.AdGroupAd(adGroupId, adId), ct);
        if (rows.Count == 0)
            return GoogleAdsExecutionResults.Missing;

        var status = GoogleAdsMappings.Status(GoogleAdsJson.String(rows[0], "adGroupAd", "status"));
        return new AdTargetState(true, status.ToString(), rows[0].GetRawText());
    }

    public Task<AdExecutionResult> ExecuteAsync(string customerId, AdAction action, bool validateOnly, CancellationToken ct) =>
        SetStatusAsync(customerId, action, ApiPaused, AdActionValues.Enabled, AdActionValues.Paused, validateOnly, ct);

    public Task<AdExecutionResult> RevertAsync(
        string customerId, AdAction action, AdExecutionResult original, bool validateOnly, CancellationToken ct) =>
        original.Outcome != AdExecutionOutcome.Succeeded
            ? Task.FromResult(GoogleAdsExecutionResults.Failed("The original pause did not succeed; there is nothing to revert."))
            : SetStatusAsync(customerId, action, ApiEnabled, AdActionValues.Paused, AdActionValues.Enabled, validateOnly, ct);

    private async Task<AdExecutionResult> SetStatusAsync(
        string customerId, AdAction action, string apiStatus, string before, string after, bool validateOnly, CancellationToken ct)
    {
        if (action.TargetLevel != AdEntityLevel.Ad || !GoogleAdsIds.TryParseComposite(action.TargetExternalId, out _, out _))
            return GoogleAdsExecutionResults.Failed(
                $"PauseAd needs an Ad target '<adGroupId>~<adId>', got {action.TargetLevel} '{action.TargetExternalId}'.");

        var request = GoogleAdsMutations.SetAdStatus(customerId, action.TargetExternalId, apiStatus, validateOnly);
        var response = await _api.MutateAsync(customerId, GoogleAdsMutateServices.AdGroupAds, request, ct);
        return GoogleAdsExecutionResults.From(response, before, after, validateOnly);
    }
}
