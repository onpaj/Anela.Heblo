using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrder;

public class UpdateManufactureOrderProductRequest
{
    public int? Id { get; set; } // For updating existing products

    public string? ProductCode { get; set; } // Optional for updates

    public string? ProductName { get; set; } // Optional for updates

    public double? PlannedQuantity { get; set; } // Optional for updates

    public decimal? ActualQuantity { get; set; } // For updating actual quantity
}