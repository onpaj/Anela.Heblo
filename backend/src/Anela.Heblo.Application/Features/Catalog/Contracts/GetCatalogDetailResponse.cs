using Anela.Heblo.Application.features.catalog.contracts;

namespace Anela.Heblo.Application.Features.Catalog.Contracts;

public class GetCatalogDetailResponse
{
    public CatalogItemDto Item { get; set; } = new();
    public CatalogHistoricalDataDto HistoricalData { get; set; } = new();
}