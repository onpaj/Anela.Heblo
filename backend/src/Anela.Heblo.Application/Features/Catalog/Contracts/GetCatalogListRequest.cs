using MediatR;
using Anela.Heblo.Application.Domain.Catalog;

namespace Anela.Heblo.Application.Features.Catalog.Contracts;

public class GetCatalogListRequest : IRequest<GetCatalogListResponse>
{
    public ProductType? Type { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? SortBy { get; set; }
    public bool SortDescending { get; set; } = false;
    public string? ProductName { get; set; }
    public string? ProductCode { get; set; }
}