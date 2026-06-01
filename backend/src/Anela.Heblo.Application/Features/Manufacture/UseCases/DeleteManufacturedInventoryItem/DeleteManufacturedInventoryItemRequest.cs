using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.DeleteManufacturedInventoryItem;

public class DeleteManufacturedInventoryItemRequest : IRequest<DeleteManufacturedInventoryItemResponse>
{
    public int Id { get; set; }
    public string? Note { get; set; }
}
