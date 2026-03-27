using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.Orders.Model;

public class OrderListResponse
{
    [JsonPropertyName("data")]
    public OrderListData Data { get; set; } = new();
}

public class OrderListData
{
    [JsonPropertyName("orders")]
    public List<OrderSummary> Orders { get; set; } = new();

    [JsonPropertyName("paginator")]
    public Paginator Paginator { get; set; } = new();
}

public class OrderSummary
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = null!;

    [JsonPropertyName("externalCode")]
    public string? ExternalCode { get; set; }

    [JsonPropertyName("status")]
    public OrderStatusSummary Status { get; set; } = new();
}

public class OrderStatusSummary
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
}

public class Paginator
{
    [JsonPropertyName("totalCount")]
    public int TotalCount { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("pageCount")]
    public int PageCount { get; set; }
}
