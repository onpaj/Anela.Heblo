using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrder;

public class UpdateManufactureOrderProductRequest
{
    [Required]
    public string ProductCode { get; set; } = null!;

    [Required]
    public string ProductName { get; set; } = null!;

    [Required]
    public double PlannedQuantity { get; set; }
}