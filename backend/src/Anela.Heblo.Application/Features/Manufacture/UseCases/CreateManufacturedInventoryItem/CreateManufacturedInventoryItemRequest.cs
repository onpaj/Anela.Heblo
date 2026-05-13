using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.CreateManufacturedInventoryItem;

public class CreateManufacturedInventoryItemRequest : IRequest<CreateManufacturedInventoryItemResponse>
{
    public required string ProductCode { get; set; }
    public required string ProductName { get; set; }
    public decimal Amount { get; set; }
    public string? LotNumber { get; set; }
    public DateOnly? ExpirationDate { get; set; }
    public int? ManufactureOrderId { get; set; }
}
