using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Anela.Heblo.Adapters.GoogleAds.Api;

namespace Anela.Heblo.Adapters.GoogleAds.Tests.Support;

internal sealed record FakeNegativeKeyword(string Collection, string ParentId, string CriterionId, string Text, string ApiMatchType)
{
    public string ResourceName(string customerId) => $"customers/{customerId}/{Collection}/{ParentId}~{CriterionId}";
}

/// <summary>
/// An in-memory Google Ads account that answers the executor's queries and applies its mutates the
/// way Google does (validateOnly changes nothing), so Execute → ReadCurrent → Revert round-trips.
/// </summary>
internal sealed class StatefulFakeGoogleAdsApi : IGoogleAdsApiClient
{
    private static readonly Regex CampaignIdFilter = new(@"campaign\.id = (\d+)");
    private static readonly Regex AdGroupIdFilter = new(@"ad_group\.id = (\d+)");
    private static readonly Regex AdIdFilter = new(@"ad_group_ad\.ad\.id = (\d+)");
    private static readonly IReadOnlyList<JsonElement> None = Array.Empty<JsonElement>();
    private long _nextCriterionId = 9000;
    private GoogleAdsMutateResult? _nextMutateResult;

    public HashSet<string> Campaigns { get; } = new();
    public HashSet<string> AdGroups { get; } = new();
    public Dictionary<string, string> AdStatuses { get; } = new();
    public List<FakeNegativeKeyword> Negatives { get; } = new();
    public List<(string Service, JsonObject Request)> Mutations { get; } = new();
    public List<GoogleAdsQuery> Queries { get; } = new();

    public void RejectNextMutate(string errorCode, string message) =>
        _nextMutateResult = GoogleAdsMutateResult.Rejected(errorCode, message, """{"error":{"code":400}}""");

    public Task<IReadOnlyList<JsonElement>> SearchAsync(string customerId, GoogleAdsQuery query, CancellationToken ct)
    {
        Queries.Add(query);
        var rows = query.Name switch
        {
            "executor_campaign" => Exists(Campaigns, Id(CampaignIdFilter, query), "campaign"),
            "executor_ad_group" => Exists(AdGroups, Id(AdGroupIdFilter, query), "adGroup"),
            "executor_campaign_negatives" => NegativeRows(customerId, GoogleAdsMutateServices.CampaignCriteria, Id(CampaignIdFilter, query), "campaignCriterion"),
            "executor_ad_group_negatives" => NegativeRows(customerId, GoogleAdsMutateServices.AdGroupCriteria, Id(AdGroupIdFilter, query), "adGroupCriterion"),
            "executor_ad_group_ad" => AdRow(customerId, $"{Id(AdGroupIdFilter, query)}~{Id(AdIdFilter, query)}"),
            _ => throw new NotSupportedException($"The executor fake does not answer '{query.Name}'."),
        };
        return Task.FromResult(rows);
    }

    public Task<GoogleAdsMutateResult> MutateAsync(string customerId, string service, JsonObject request, CancellationToken ct)
    {
        Mutations.Add((service, (JsonObject)request.DeepClone()));
        if (_nextMutateResult is { } rejected)
        {
            _nextMutateResult = null;
            return Task.FromResult(rejected);
        }

        var validateOnly = request["validateOnly"]?.GetValue<bool>() == true;
        var operation = request["operations"]!.AsArray()[0]!.AsObject();
        var resourceName = service == GoogleAdsMutateServices.AdGroupAds
            ? ApplyAdUpdate(operation, validateOnly)
            : operation.ContainsKey("remove")
                ? ApplyRemove(operation, validateOnly)
                : ApplyCreate(customerId, service, operation, validateOnly);

        return Task.FromResult(validateOnly
            ? GoogleAdsMutateResult.Success(Array.Empty<string>(), "{}")
            : GoogleAdsMutateResult.Success(new[] { resourceName },
                new JsonObject { ["results"] = new JsonArray(new JsonObject { ["resourceName"] = resourceName }) }.ToJsonString()));
    }

    private static string Id(Regex filter, GoogleAdsQuery query) =>
        filter.Match(query.Gaql) is { Success: true } match
            ? match.Groups[1].Value
            : throw new InvalidOperationException($"Query '{query.Name}' has no {filter} filter: {query.Gaql}");

    private static JsonElement Element(JsonNode node) => JsonSerializer.SerializeToElement(node);

    private static IReadOnlyList<JsonElement> Exists(HashSet<string> ids, string id, string property) =>
        ids.Contains(id)
            ? new[] { Element(new JsonObject { [property] = new JsonObject { ["id"] = id, ["status"] = "ENABLED" } }) }
            : None;

    private IReadOnlyList<JsonElement> NegativeRows(string customerId, string collection, string parentId, string property) =>
        Negatives.Where(n => n.Collection == collection && n.ParentId == parentId)
            .Select(n => Element(new JsonObject
            {
                [property] = new JsonObject
                {
                    ["resourceName"] = n.ResourceName(customerId),
                    ["keyword"] = new JsonObject { ["text"] = n.Text, ["matchType"] = n.ApiMatchType },
                },
            }))
            .ToList();

    private IReadOnlyList<JsonElement> AdRow(string customerId, string adGroupAdId) =>
        AdStatuses.TryGetValue(adGroupAdId, out var status)
            ? new[]
            {
                Element(new JsonObject
                {
                    ["adGroupAd"] = new JsonObject
                    {
                        ["resourceName"] = $"customers/{customerId}/adGroupAds/{adGroupAdId}",
                        ["status"] = status,
                    },
                }),
            }
            : None;

    private string ApplyAdUpdate(JsonObject operation, bool validateOnly)
    {
        var update = operation["update"]!.AsObject();
        var resourceName = update["resourceName"]!.GetValue<string>();
        if (!validateOnly)
            AdStatuses[resourceName[(resourceName.LastIndexOf('/') + 1)..]] = update["status"]!.GetValue<string>();
        return resourceName;
    }

    private string ApplyCreate(string customerId, string service, JsonObject operation, bool validateOnly)
    {
        var create = operation["create"]!.AsObject();
        var parent = (create["campaign"] ?? create["adGroup"])!.GetValue<string>();
        var negative = new FakeNegativeKeyword(
            service,
            parent[(parent.LastIndexOf('/') + 1)..],
            (_nextCriterionId++).ToString(CultureInfo.InvariantCulture),
            create["keyword"]!["text"]!.GetValue<string>(),
            create["keyword"]!["matchType"]!.GetValue<string>());
        if (!validateOnly)
            Negatives.Add(negative);
        return negative.ResourceName(customerId);
    }

    private string ApplyRemove(JsonObject operation, bool validateOnly)
    {
        var resourceName = operation["remove"]!.GetValue<string>();
        if (!validateOnly)
            Negatives.RemoveAll(n => resourceName.EndsWith($"/{n.Collection}/{n.ParentId}~{n.CriterionId}", StringComparison.Ordinal));
        return resourceName;
    }
}
