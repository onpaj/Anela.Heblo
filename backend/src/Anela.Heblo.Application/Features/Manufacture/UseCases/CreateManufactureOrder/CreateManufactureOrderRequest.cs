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

    public List<CreateManufactureOrderProductRequest> Products { get; set; } = new();

    [Required]
    public DateOnly SemiProductPlannedDate { get; set; }

    [Required]
    public DateOnly ProductPlannedDate { get; set; }

    public string? ResponsiblePerson { get; set; }
}

public class CreateManufactureOrderProductRequest
{
    [Required]
    public string ProductCode { get; set; } = null!;

    [Required]
    public string ProductName { get; set; } = null!;

    [Required]
    public double PlannedQuantity { get; set; }
}