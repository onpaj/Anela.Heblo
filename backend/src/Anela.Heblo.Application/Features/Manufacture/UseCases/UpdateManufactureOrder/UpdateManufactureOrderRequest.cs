using MediatR;
using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrder;

public class UpdateManufactureOrderRequest : IRequest<UpdateManufactureOrderResponse>
{
    [Required]
    public int Id { get; set; }

    [Required]
    public DateOnly SemiProductPlannedDate { get; set; }

    [Required]
    public DateOnly ProductPlannedDate { get; set; }

    public string? ResponsiblePerson { get; set; }

    public UpdateManufactureOrderSemiProductRequest? SemiProduct { get; set; }

    public List<UpdateManufactureOrderProductRequest> Products { get; set; } = new();

    public string? NewNote { get; set; }
}

public class UpdateManufactureOrderSemiProductRequest
{
    public string? LotNumber { get; set; } // Šarže

    public DateOnly? ExpirationDate { get; set; } // Expirace
}

public class UpdateManufactureOrderProductRequest
{
    [Required]
    public string ProductCode { get; set; } = null!;

    [Required]
    public string ProductName { get; set; } = null!;

    [Required]
    public double PlannedQuantity { get; set; }
}