using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.ListLots;

public class ListLotsRequest : IRequest<ListLotsResponse>
{
    public string? MaterialCode { get; set; }
    public DateOnly? ExpirationFrom { get; set; }
    public DateOnly? ExpirationTo { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
