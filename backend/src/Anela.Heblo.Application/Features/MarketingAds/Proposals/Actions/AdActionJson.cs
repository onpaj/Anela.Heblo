using System.Text.Json;
using System.Text.Json.Serialization;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Actions;

/// <summary>Format of AdProposalVersion.ActionsJson: camelCase, enums by name.</summary>
public static class AdActionJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(IReadOnlyList<AdAction> actions) => JsonSerializer.Serialize(actions, Options);

    public static IReadOnlyList<AdAction> Deserialize(string json) =>
        JsonSerializer.Deserialize<List<AdAction>>(json, Options)
        ?? throw new JsonException("Proposal actions JSON deserialised to null.");
}
