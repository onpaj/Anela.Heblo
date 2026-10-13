using Anela.Heblo.Domain.Features.Catalog;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetProductMargins;

public class GetProductMarginsRequest : IRequest<GetProductMarginsResponse>
{
    public string? ProductCode { get; set; }
    public string? ProductName { get; set; }
    public ProductType? ProductType { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? SortBy { get; set; }
    public bool SortDescending { get; set; } = false;

    /// <summary>
    /// When true, only products with at least one sale in the last year are returned.
    /// Products without sales have no meaningful margin and would otherwise crowd the top of the list.
    /// </summary>
    public bool OnlyWithSales { get; set; } = false;
}