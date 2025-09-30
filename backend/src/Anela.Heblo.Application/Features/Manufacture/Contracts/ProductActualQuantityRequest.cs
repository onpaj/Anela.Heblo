using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.Manufacture.Services;

public class ProductActualQuantityRequest
{
    [Required]
    public int Id { get; set; }

    [Required]
    [Range(0, double.MaxValue, ErrorMessage = "ActualQuantity must be 0 or greater")]
    public decimal ActualQuantity { get; set; }
}