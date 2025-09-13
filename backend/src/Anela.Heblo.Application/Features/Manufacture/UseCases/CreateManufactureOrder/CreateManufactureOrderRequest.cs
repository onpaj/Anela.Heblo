using MediatR;
using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.CreateManufactureOrder;

public class CreateManufactureOrderRequest : IRequest<CreateManufactureOrderResponse>
{
    [Required]
    public string ProductCode { get; set; } = null!;

    [Required]
    public string ProductName { get; set; } = null!;

    [Required]
    public double OriginalBatchSize { get; set; }

    [Required]
    public double NewBatchSize { get; set; }

    [Required]
    public double ScaleFactor { get; set; }

    [Required]
    public List<CreateManufactureOrderIngredientRequest> Ingredients { get; set; } = new();

    [Required]
    public DateOnly SemiProductPlannedDate { get; set; }

    [Required]
    public DateOnly ProductPlannedDate { get; set; }

    public string? ResponsiblePerson { get; set; }
}

public class CreateManufactureOrderIngredientRequest
{
    [Required]
    public string ProductCode { get; set; } = null!;

    [Required]
    public string ProductName { get; set; } = null!;

    [Required]
    public double OriginalAmount { get; set; }

    [Required]
    public double CalculatedAmount { get; set; }
}