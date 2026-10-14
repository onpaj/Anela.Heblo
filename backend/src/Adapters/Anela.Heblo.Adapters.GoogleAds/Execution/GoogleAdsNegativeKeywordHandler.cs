using System.Text.Json;
using System.Text.RegularExpressions;
using Anela.Heblo.Adapters.GoogleAds.Api;
using Anela.Heblo.Adapters.GoogleAds.Reporting;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Adapters.GoogleAds.Execution;

/// <summary>
/// AddNegativeKeyword on a campaign (campaign_criterion) or ad group (ad_group_criterion). Presence is
/// decided in C# — case-insensitive, whitespace-collapsed text plus equal match type — so agent text
/// never reaches GAQL. Revert removes the criterion named by the original PlatformResourceId.
/// </summary>
internal sealed class GoogleAdsNegativeKeywordHandler
{
    private const int MaxKeywordLength = 80;
    private const int MaxKeywordWords = 10;
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.CultureInvariant);
    private static readonly Regex CriterionId = new(@"\A[0-9]+\z", RegexOptions.CultureInvariant);

    private readonly IGoogleAdsApiClient _api;

    public GoogleAdsNegativeKeywordHandler(IGoogleAdsApiClient api) => _api = api;

    public async Task<AdTargetState> ReadCurrentAsync(string customerId, AdAction action, CancellationToken ct)
    {
        if (TryParse(action, out var target) is not null)
            return GoogleAdsExecutionResults.Missing;

        var parent = await _api.SearchAsync(customerId, target!.ParentQuery, ct);
        if (parent.Count == 0)
            return GoogleAdsExecutionResults.Missing;

        var negatives = await _api.SearchAsync(customerId, target.NegativesQuery, ct);
        var match = negatives.FirstOrDefault(row => target.Matches(row));
        return match.ValueKind == JsonValueKind.Undefined
            ? new AdTargetState(true, AdActionValues.Absent, null)
            : new AdTargetState(true, AdActionValues.Present, match.GetRawText());
    }

    public async Task<AdExecutionResult> ExecuteAsync(string customerId, AdAction action, bool validateOnly, CancellationToken ct)
    {
        if (TryParse(action, out var target) is { } error)
            return GoogleAdsExecutionResults.Failed(error);

        var request = target!.Level == AdEntityLevel.Campaign
            ? GoogleAdsMutations.CreateCampaignNegative(customerId, target.ParentId, target.Text, target.MatchType, validateOnly)
            : GoogleAdsMutations.CreateAdGroupNegative(customerId, target.ParentId, target.Text, target.MatchType, validateOnly);
        var response = await _api.MutateAsync(customerId, target.Service, request, ct);
        return GoogleAdsExecutionResults.From(response, AdActionValues.Absent, AdActionValues.Present, validateOnly);
    }

    public async Task<AdExecutionResult> RevertAsync(
        string customerId, AdAction action, AdExecutionResult original, bool validateOnly, CancellationToken ct)
    {
        if (original.Outcome != AdExecutionOutcome.Succeeded || original.PlatformResourceId is null)
            return GoogleAdsExecutionResults.Failed("The negative keyword was never created; there is nothing to remove.");

        if (TryParse(action, out var target) is { } error)
            return GoogleAdsExecutionResults.Failed(error);

        var prefix = $"customers/{customerId}/{target!.Service}/{target.ParentId}~";
        var resource = original.PlatformResourceId;
        if (!resource.StartsWith(prefix, StringComparison.Ordinal) || !CriterionId.IsMatch(resource[prefix.Length..]))
            return GoogleAdsExecutionResults.Failed(
                $"'{resource}' is not a negative keyword criterion of {target.Level} {target.ParentId} in account {customerId}.");

        var response = await _api.MutateAsync(
            customerId, target.Service, GoogleAdsMutations.Remove(resource, validateOnly), ct);
        return GoogleAdsExecutionResults.From(response, AdActionValues.Present, AdActionValues.Absent, validateOnly);
    }

    /// <returns>null and a target when the action is well-formed; otherwise the reason it is not.</returns>
    private static string? TryParse(AdAction action, out NegativeKeywordTarget? target)
    {
        target = null;
        if (action.TargetLevel is not (AdEntityLevel.Campaign or AdEntityLevel.AdGroup))
            return $"AddNegativeKeyword targets a Campaign or AdGroup, got {action.TargetLevel}.";
        if (!GoogleAdsIds.IsNumericId(action.TargetExternalId))
            return $"Target '{action.TargetExternalId}' is not a numeric Google Ads id.";

        var text = Normalize(action.Payload.TryGetValue(AdActionPayloadKeys.Text, out var t) ? t : null);
        if (text.Length is 0 or > MaxKeywordLength || text.Split(' ').Length > MaxKeywordWords)
            return $"Negative keyword text must be 1-{MaxKeywordLength} characters and at most {MaxKeywordWords} words.";

        var matchTypeName = action.Payload.TryGetValue(AdActionPayloadKeys.MatchType, out var m) ? m : null;
        if (matchTypeName is null || !Enum.GetNames<KeywordMatchType>().Contains(matchTypeName))
            return $"Unknown match type '{matchTypeName}'; expected one of {string.Join(", ", Enum.GetNames<KeywordMatchType>())}.";

        target = new NegativeKeywordTarget(
            action.TargetLevel, action.TargetExternalId, text, Enum.Parse<KeywordMatchType>(matchTypeName));
        return null;
    }

    private static string Normalize(string? value) => Whitespace.Replace((value ?? string.Empty).Trim(), " ");

    private sealed record NegativeKeywordTarget(AdEntityLevel Level, string ParentId, string Text, KeywordMatchType MatchType)
    {
        private string CriterionProperty => Level == AdEntityLevel.Campaign ? "campaignCriterion" : "adGroupCriterion";

        public string Service => Level == AdEntityLevel.Campaign
            ? GoogleAdsMutateServices.CampaignCriteria
            : GoogleAdsMutateServices.AdGroupCriteria;

        public GoogleAdsQuery ParentQuery => Level == AdEntityLevel.Campaign
            ? GoogleAdsExecutorQueries.Campaign(ParentId)
            : GoogleAdsExecutorQueries.AdGroup(ParentId);

        public GoogleAdsQuery NegativesQuery => Level == AdEntityLevel.Campaign
            ? GoogleAdsExecutorQueries.CampaignNegatives(ParentId)
            : GoogleAdsExecutorQueries.AdGroupNegatives(ParentId);

        public bool Matches(JsonElement row) =>
            string.Equals(Normalize(GoogleAdsJson.String(row, CriterionProperty, "keyword", "text")), Text, StringComparison.OrdinalIgnoreCase)
            && GoogleAdsMappings.KeywordMatch(GoogleAdsJson.String(row, CriterionProperty, "keyword", "matchType")) == MatchType;
    }
}
