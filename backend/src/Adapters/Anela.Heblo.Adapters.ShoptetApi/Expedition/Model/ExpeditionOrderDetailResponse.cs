using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.Expedition.Model;

public class ExpeditionOrderDetailResponse
{
    [JsonPropertyName("data")]
    public ExpeditionOrderDetailData Data { get; set; } = new();
}

public class ExpeditionOrderDetailData
{
    [JsonPropertyName("order")]
    public ExpeditionOrderDetail Order { get; set; } = new();
}

public class ExpeditionOrderDetail
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = null!;

    [JsonPropertyName("fullName")]
    public string? FullName { get; set; }

    [JsonPropertyName("company")]
    public string? Company { get; set; }

    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    [JsonPropertyName("deliveryAddress")]
    public ExpeditionAddress? DeliveryAddress { get; set; }

    [JsonPropertyName("billingAddress")]
    public ExpeditionAddress? BillingAddress { get; set; }

    [JsonPropertyName("items")]
    public List<ExpeditionOrderItemDto> Items { get; set; } = new();

    [JsonPropertyName("completion")]
    public List<ExpeditionCompletionItemDto> Completion { get; set; } = new();
}

public class ExpeditionAddress
{
    [JsonPropertyName("fullName")]
    public string? FullName { get; set; }

    [JsonPropertyName("company")]
    public string? Company { get; set; }

    [JsonPropertyName("street")]
    public string? Street { get; set; }

    [JsonPropertyName("houseNumber")]
    public string? HouseNumber { get; set; }

    [JsonPropertyName("city")]
    public string? City { get; set; }

    [JsonPropertyName("zip")]
    public string? Zip { get; set; }
}

public class ExpeditionCompletionItemDto
{
    [JsonPropertyName("itemType")]
    public string ItemType { get; set; } = null!;

    [JsonPropertyName("itemId")]
    public long ItemId { get; set; }

    [JsonPropertyName("parentProductSetItemId")]
    public long? ParentProductSetItemId { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("variantName")]
    public string? VariantName { get; set; }

    [JsonPropertyName("amount")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? Amount { get; set; }

    [JsonPropertyName("amountUnit")]
    public string? Unit { get; set; }
}

public class ExpeditionOrderItemDto
{
    [JsonPropertyName("itemType")]
    public string ItemType { get; set; } = null!;

    [JsonPropertyName("itemId")]
    public long ItemId { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("variantName")]
    public string? VariantName { get; set; }

    [JsonPropertyName("stockLocation")]
    public string? WarehousePosition { get; set; }

    [JsonPropertyName("amount")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal? Amount { get; set; }

    [JsonPropertyName("unit")]
    public string? Unit { get; set; }

    [JsonPropertyName("itemPriceWithVat")]
    public string? ItemPriceWithVat { get; set; }

    [JsonPropertyName("stockStatus")]
    public ExpeditionStockStatus? StockStatus { get; set; }
}

public class ExpeditionStockStatus
{
    [JsonPropertyName("stockCount")]
    public int StockCount { get; set; }

    [JsonPropertyName("allDemand")]
    public int AllDemand { get; set; }
}
