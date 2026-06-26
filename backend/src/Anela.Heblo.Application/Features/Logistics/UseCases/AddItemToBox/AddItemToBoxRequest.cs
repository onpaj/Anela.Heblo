using System.ComponentModel.DataAnnotations;
using MediatR;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.AddItemToBox;

public class AddItemToBoxRequest : IRequest<AddItemToBoxResponse>
{
    public int BoxId { get; set; }

    [Required]
    public string ProductCode { get; set; } = null!;

    [Required]
    public string ProductName { get; set; } = null!;

    [Required]
    [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than 0")]
    public double Amount { get; set; }

    public int? SourceInventoryId { get; set; }
    public string? LotNumber { get; set; }
    public DateOnly? ExpirationDate { get; set; }
    public bool AllowNegativeStock { get; set; }
}