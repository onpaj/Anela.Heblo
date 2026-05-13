using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.CreateManufacturedInventoryItem;

public class CreateManufacturedInventoryItemRequest : IRequest<CreateManufacturedInventoryItemResponse>
{
    public string ProductCode { get; set; } = null!;
    public string ProductName { get; set; } = null!;
    public decimal Amount { get; set; }
    public string? LotNumber { get; set; }
    public DateOnly? ExpirationDate { get; set; }
    public int? ManufactureOrderId { get; set; }
}
