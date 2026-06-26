using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.Orders.Model;

/// <summary>
/// Body for PATCH /api/orders/{code}/notes when only additionalFields need updating.
/// Sending only this slice leaves customerRemark, eshopRemark, and trackingNumber untouched.
/// </summary>
public class UpdateAdditionalFieldRequest
{
    [JsonPropertyName("data")]
    public required UpdateAdditionalFieldData Data { get; init; }
}

public class UpdateAdditionalFieldData
{
    [JsonPropertyName("additionalFields")]
    public required IReadOnlyList<AdditionalFieldEntry> AdditionalFields { get; init; }
}

public class AdditionalFieldEntry
{
    [JsonPropertyName("index")]
    public required int Index { get; init; }

    [JsonPropertyName("text")]
    public string? Text { get; init; }
}
