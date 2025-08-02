namespace Anela.Heblo.Application.Features.Catalog.Model;

public class GetCatalogDetailResponse
{
    public CatalogItemDto Item { get; set; } = new();
    public CatalogHistoricalDataDto HistoricalData { get; set; } = new();
}