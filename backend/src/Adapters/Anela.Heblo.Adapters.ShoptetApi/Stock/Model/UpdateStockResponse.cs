using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.Stock.Model;

/// <summary>
/// Response from PATCH /api/stocks/{stockId}/movements.
/// Shoptet returns 200 OK even for partial failures; check Errors for per-product issues.
/// If all records fail, Shoptet returns 400 and Errors is also populated.
/// </summary>
public class UpdateStockResponse
{
    [JsonPropertyName("errors")]
    public List<UpdateStockError>? Errors { get; set; }
}

public class UpdateStockError
{
    [JsonPropertyName("errorCode")]
    public string ErrorCode { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    /// <summary>Instance is the productCode that caused the error.</summary>
    [JsonPropertyName("instance")]
    public string Instance { get; set; } = string.Empty;
}
