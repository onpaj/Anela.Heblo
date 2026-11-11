using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.Products.Model;

public class FlagListResponse
{
    [JsonPropertyName("data")]
    public FlagListData? Data { get; set; }
}

public class FlagListData
{
    [JsonPropertyName("flags")]
    public List<FlagItem> Flags { get; set; } = new();
}

public class FlagItem
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = "";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";
}
