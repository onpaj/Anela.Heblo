using System.ComponentModel.DataAnnotations;
using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.SubmitManufactureStockTaking;

public class SubmitManufactureStockTakingRequest : IRequest<SubmitManufactureStockTakingResponse>
{
    [Required(ErrorMessage = "Product code is required")]
    [StringLength(50, ErrorMessage = "Product code cannot exceed 50 characters")]
    public string ProductCode { get; set; } = null!;

    // For simple stock taking (non-lot materials)
    [Range(0, 999999.99, ErrorMessage = "Target amount must be between 0 and 999999.99")]
    public decimal? TargetAmount { get; set; }

    public bool SoftStockTaking { get; set; } = true;

    // For lot-based stock taking (materials with lots)
    public List<ManufactureStockTakingLotDto>? Lots { get; set; }
}