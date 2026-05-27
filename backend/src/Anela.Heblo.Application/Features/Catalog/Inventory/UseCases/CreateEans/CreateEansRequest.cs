using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateEans;

public class CreateEansRequest : IRequest<CreateEansResponse>
{
    public int LotId { get; set; }
    public List<CreateEanItem> Items { get; set; } = new();
}

public class CreateEanItem
{
    public decimal Amount { get; set; }
    public string Unit { get; set; } = null!;
}
