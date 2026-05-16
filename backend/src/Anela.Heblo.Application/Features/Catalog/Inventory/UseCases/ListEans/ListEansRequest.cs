using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.ListEans;

public class ListEansRequest : IRequest<ListEansResponse>
{
    public int? LotId { get; set; }
    public string? MaterialCode { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
