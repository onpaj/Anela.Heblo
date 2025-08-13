using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.Model;

public class GetProductMarginsRequest : IRequest<GetProductMarginsResponse>
{
    public string? ProductCode { get; set; }
    public string? ProductName { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}