using System.Text.Json.Nodes;
using Anela.Heblo.Adapters.GoogleAds.Reporting;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Adapters.GoogleAds.Execution;

/// <summary>Mutate request bodies (integration doc §3). Built as JSON nodes, so text is always escaped.</summary>
internal static class GoogleAdsMutations
{
    public static JsonObject SetAdStatus(string customerId, string adGroupAdId, string apiStatus, bool validateOnly) =>
        Wrap(new JsonObject
        {
            ["updateMask"] = "status",
            ["update"] = new JsonObject
            {
                ["resourceName"] = $"customers/{customerId}/adGroupAds/{adGroupAdId}",
                ["status"] = apiStatus,
            },
        }, validateOnly);

    public static JsonObject CreateCampaignNegative(
        string customerId, string campaignId, string text, KeywordMatchType matchType, bool validateOnly) =>
        Wrap(new JsonObject
        {
            ["create"] = new JsonObject
            {
                ["campaign"] = $"customers/{customerId}/campaigns/{campaignId}",
                ["negative"] = true,
                ["keyword"] = Keyword(text, matchType),
            },
        }, validateOnly);

    public static JsonObject CreateAdGroupNegative(
        string customerId, string adGroupId, string text, KeywordMatchType matchType, bool validateOnly) =>
        Wrap(new JsonObject
        {
            ["create"] = new JsonObject
            {
                ["adGroup"] = $"customers/{customerId}/adGroups/{adGroupId}",
                ["negative"] = true,
                ["keyword"] = Keyword(text, matchType),
            },
        }, validateOnly);

    public static JsonObject Remove(string resourceName, bool validateOnly) =>
        Wrap(new JsonObject { ["remove"] = resourceName }, validateOnly);

    private static JsonObject Keyword(string text, KeywordMatchType matchType) => new()
    {
        ["text"] = text,
        ["matchType"] = GoogleAdsMappings.ToApiMatchType(matchType),
    };

    private static JsonObject Wrap(JsonObject operation, bool validateOnly) => new()
    {
        ["operations"] = new JsonArray(operation),
        ["partialFailure"] = false,
        ["validateOnly"] = validateOnly,
    };
}
