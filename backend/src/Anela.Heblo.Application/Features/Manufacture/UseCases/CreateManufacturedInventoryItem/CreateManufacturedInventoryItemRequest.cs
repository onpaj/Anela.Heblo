using System.ComponentModel.DataAnnotations;
using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.CreateManufacturedInventoryItem;

public class CreateManufacturedInventoryItemRequest : IRequest<CreateManufacturedInventoryItemResponse>
{
    public required string ProductCode { get; set; }
    public required string ProductName { get; set; }
    [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than 0")]
    public decimal Amount { get; set; }
    public string? LotNumber { get; set; }
    public DateOnly? ExpirationDate { get; set; }
    public int? ManufactureOrderId { get; set; }
}
