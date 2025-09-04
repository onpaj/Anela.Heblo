using Anela.Heblo.Domain.Features.Catalog;
using MediatR;

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

    /// <summary>
    /// Search term for autocomplete - searches in both ProductName and ProductCode with OR logic
    /// </summary>
    public string? SearchTerm { get; set; }
}