using Anela.Heblo.Application.Features.Catalog.Contracts;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetCatalogDetail;

public class GetCatalogDetailResponse
{
    public CatalogItemDto Item { get; set; } = new();
    public CatalogHistoricalDataDto HistoricalData { get; set; } = new();
}