using System.ComponentModel.DataAnnotations;
using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.SubmitStockTaking;

public class SubmitStockTakingRequest : IRequest<SubmitStockTakingResponse>
{
    [Required(ErrorMessage = "Product code is required")]
    [StringLength(50, ErrorMessage = "Product code cannot exceed 50 characters")]
    public string ProductCode { get; set; } = null!;

    [Required(ErrorMessage = "Target amount is required")]
    [Range(0, 999999.99, ErrorMessage = "Target amount must be between 0 and 999999.99")]
    public decimal TargetAmount { get; set; }

    public bool SoftStockTaking { get; set; } = true;
}