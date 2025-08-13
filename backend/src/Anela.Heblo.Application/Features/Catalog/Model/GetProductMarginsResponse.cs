namespace Anela.Heblo.Application.Features.Catalog.Model;

public class GetProductMarginsResponse
{
    public List<ProductMarginDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public class ProductMarginDto
{
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal? PriceWithVat { get; set; }
    public decimal? PurchasePrice { get; set; }
    public decimal? AverageCost { get; set; }
    public decimal? Cost30Days { get; set; }
    public decimal? AverageMargin { get; set; }
    public decimal? Margin30Days { get; set; }
}