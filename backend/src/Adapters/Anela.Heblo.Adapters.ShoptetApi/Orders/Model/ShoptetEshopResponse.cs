using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.Orders.Model;

public class ShoptetEshopResponse
{
    [JsonPropertyName("data")]
    public ShoptetEshopData? Data { get; set; }
}

public class ShoptetEshopData
{
    [JsonPropertyName("eshop")]
    public ShoptetEshopDetail? Eshop { get; set; }
}

public class ShoptetEshopDetail
{
    [JsonPropertyName("orderStatuses")]
    public List<ShoptetOrderStatus> OrderStatuses { get; set; } = new();
}

public class ShoptetOrderStatus
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}
