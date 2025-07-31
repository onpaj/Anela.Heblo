using MediatR;
using Anela.Heblo.Application.Domain.Catalog;

namespace Anela.Heblo.Application.features.catalog.contracts;

public class GetCatalogListRequest : IRequest<GetCatalogListResponse>
{
    public ProductType? Type { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? SortBy { get; set; }
    public bool SortDescending { get; set; } = false;
}