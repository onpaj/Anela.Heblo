using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufacturedInventoryItem;

public class UpdateManufacturedInventoryItemRequest : IRequest<UpdateManufacturedInventoryItemResponse>
{
    public int Id { get; set; }
    public decimal NewAmount { get; set; }
    public string? Note { get; set; }
}
