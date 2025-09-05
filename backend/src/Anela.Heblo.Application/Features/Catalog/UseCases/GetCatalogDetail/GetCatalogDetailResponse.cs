using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetCatalogDetail;

public class GetCatalogDetailResponse : BaseResponse
{
    public CatalogItemDto Item { get; set; } = new();
    public CatalogHistoricalDataDto HistoricalData { get; set; } = new();
}