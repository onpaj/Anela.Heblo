using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.Stock.Model;

/// <summary>
/// Body for PATCH /api/stocks/{stockId}/movements.
/// Shoptet's schema uses additionalProperties: false — only productCode + one of
/// amountChange/quantity/realStock are accepted.
/// </summary>
public class UpdateStockRequest
{
    [JsonPropertyName("data")]
    public List<UpdateStockItem> Data { get; set; } = new();
}

public class UpdateStockItem
{
    [JsonPropertyName("productCode")]
    public string ProductCode { get; set; } = string.Empty;

    [JsonPropertyName("amountChange")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? AmountChange { get; set; }

    [JsonPropertyName("realStock")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? RealStock { get; set; }
}
