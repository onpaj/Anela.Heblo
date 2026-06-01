using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.SubmitManufactureStockTaking;

public class ManufactureStockTakingLotDto
{
    public string? LotCode { get; set; }

    public DateOnly? Expiration { get; set; }

    [Required(ErrorMessage = "Amount is required")]
    [Range(0, 999999.99, ErrorMessage = "Amount must be between 0 and 999999.99")]
    public decimal Amount { get; set; }

    public bool SoftStockTaking { get; set; } = true;
}