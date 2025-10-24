using MediatR;
using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Domain.Features.Manufacture;

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
    public DateOnly PlannedDate { get; set; }

    public string? ResponsiblePerson { get; set; }

    public ManufactureType ManufactureType { get; set; } = ManufactureType.MultiPhase;
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