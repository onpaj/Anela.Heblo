using System.Text.Json.Serialization;

namespace Anela.Heblo.Application.Features.Catalog.Model;

public class CatalogSalesRecordDto
{
    [JsonPropertyName("year")]
    public int Year { get; set; }

    [JsonPropertyName("month")]
    public int Month { get; set; }

    [JsonPropertyName("amountTotal")]
    public double AmountTotal { get; set; }

    [JsonPropertyName("amountB2B")]
    public double AmountB2B { get; set; }

    [JsonPropertyName("amountB2C")]
    public double AmountB2C { get; set; }

    [JsonPropertyName("sumTotal")]
    public decimal SumTotal { get; set; }

    [JsonPropertyName("sumB2B")]
    public decimal SumB2B { get; set; }

    [JsonPropertyName("sumB2C")]
    public decimal SumB2C { get; set; }
}