using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.Analytics.Model;

/// <summary>
/// GET /api/orders — the list endpoint. It carries no items[], so the sync uses it only to
/// enumerate order codes inside a creation-time window; line items come from the detail call.
/// </summary>
public class ShoptetOrderCodeListResponse
{
    [JsonPropertyName("data")]
    public ShoptetOrderCodeListData? Data { get; set; }
}

public class ShoptetOrderCodeListData
{
    [JsonPropertyName("orders")]
    public List<ShoptetOrderCodeDto> Orders { get; set; } = new();

    [JsonPropertyName("paginator")]
    public ShoptetPaginatorDto? Paginator { get; set; }
}

public class ShoptetOrderCodeDto
{
    [JsonPropertyName("code")] public string Code { get; set; } = "";
    [JsonPropertyName("changeTime")][JsonConverter(typeof(ShoptetDateTimeOffsetConverter))] public DateTimeOffset? ChangeTime { get; set; }
    [JsonPropertyName("creationTime")][JsonConverter(typeof(ShoptetDateTimeOffsetConverter))] public DateTimeOffset? CreationTime { get; set; }
}

public class ShoptetPaginatorDto
{
    [JsonPropertyName("totalCount")] public int TotalCount { get; set; }
    [JsonPropertyName("page")] public int Page { get; set; }
    [JsonPropertyName("pageCount")] public int PageCount { get; set; }
    [JsonPropertyName("itemsOnPage")] public int ItemsOnPage { get; set; }
    [JsonPropertyName("itemsPerPage")] public int ItemsPerPage { get; set; }
}

/// <summary>GET /api/orders/changes — the 30-day edit/delete log.</summary>
public class ShoptetOrderChangeListResponse
{
    [JsonPropertyName("data")]
    public ShoptetOrderChangeListData? Data { get; set; }
}

public class ShoptetOrderChangeListData
{
    [JsonPropertyName("changes")] public List<ShoptetOrderChangeDto> Changes { get; set; } = new();
    [JsonPropertyName("paginator")] public ShoptetPaginatorDto? Paginator { get; set; }
}

public class ShoptetOrderChangeDto
{
    [JsonPropertyName("code")] public string Code { get; set; } = "";
    [JsonPropertyName("changeTime")][JsonConverter(typeof(ShoptetDateTimeOffsetConverter))] public DateTimeOffset? ChangeTime { get; set; }
    /// <summary>"edit" or "delete".</summary>
    [JsonPropertyName("changeType")] public string? ChangeType { get; set; }
}
