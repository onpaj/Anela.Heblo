using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices.Model;

public class InvoiceListResponse
{
    [JsonPropertyName("data")]
    public InvoiceListData Data { get; set; } = new();
}

public class InvoiceListData
{
    [JsonPropertyName("invoices")]
    public List<ShoptetInvoiceDto> Invoices { get; set; } = new();

    [JsonPropertyName("paginator")]
    public InvoicePaginator Paginator { get; set; } = new();
}

public class InvoicePaginator
{
    [JsonPropertyName("totalCount")]
    public int TotalCount { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("pageCount")]
    public int PageCount { get; set; }
}

public class InvoiceDetailResponse
{
    [JsonPropertyName("data")]
    public InvoiceDetailData Data { get; set; } = new();
}

public class InvoiceDetailData
{
    [JsonPropertyName("invoice")]
    public ShoptetInvoiceDto Invoice { get; set; } = new();
}
