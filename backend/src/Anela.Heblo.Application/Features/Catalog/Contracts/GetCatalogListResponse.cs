namespace Anela.Heblo.Application.features.catalog.contracts;

public class GetCatalogListResponse
{
    public List<CatalogItemDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}