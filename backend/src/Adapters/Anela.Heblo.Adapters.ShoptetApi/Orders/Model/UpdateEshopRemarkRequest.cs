using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.Orders.Model;

/// <summary>
/// Body for PATCH /api/orders/{code}/notes (Shoptet operationId: updateRemarksForOrder).
/// The endpoint accepts customerRemark, eshopRemark, trackingNumber, and additionalFields[] —
/// this project only updates eshopRemark, so only that property is modelled. Omitted fields
/// are left unchanged by the Shoptet API.
/// </summary>
public class UpdateEshopRemarkRequest
{
    [JsonPropertyName("data")]
    public UpdateEshopRemarkData Data { get; set; } = new();
}

public class UpdateEshopRemarkData
{
    [JsonPropertyName("eshopRemark")]
    public string EshopRemark { get; set; } = string.Empty;
}
