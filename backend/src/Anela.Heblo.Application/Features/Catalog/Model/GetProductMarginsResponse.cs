namespace Anela.Heblo.Application.Features.Catalog.Model;

public class GetProductMarginsResponse
{
    public List<ProductMarginDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}