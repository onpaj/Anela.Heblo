using System.Text.Json.Serialization;

namespace Anela.Heblo.Application.Features.ShoptetOrders.UseCases.BlockOrderProcessing;

public class BlockOrderRequest
{
    [JsonPropertyName("note")]
    public string Note { get; set; } = string.Empty;
}
