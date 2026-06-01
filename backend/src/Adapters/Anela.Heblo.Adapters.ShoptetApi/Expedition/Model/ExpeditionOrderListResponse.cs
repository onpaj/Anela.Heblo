using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.Expedition.Model;

public class ExpeditionOrderListResponse
{
    [JsonPropertyName("data")]
    public ExpeditionOrderListData Data { get; set; } = new();
}

public class ExpeditionOrderListData
{
    [JsonPropertyName("orders")]
    public List<ExpeditionOrderSummary> Orders { get; set; } = new();

    [JsonPropertyName("paginator")]
    public ExpeditionPaginator Paginator { get; set; } = new();
}

public class ExpeditionPaginator
{
    [JsonPropertyName("pageCount")]
    public int PageCount { get; set; }
}

public class ExpeditionOrderSummary
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = null!;

    [JsonPropertyName("shipping")]
    public ExpeditionShippingSummary? Shipping { get; set; }
}

public class ExpeditionShippingSummary
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
}
