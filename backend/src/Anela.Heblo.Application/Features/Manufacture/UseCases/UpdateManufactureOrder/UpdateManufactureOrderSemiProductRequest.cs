namespace Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrder;

public class UpdateManufactureOrderSemiProductRequest
{
    public decimal? PlannedQuantity { get; set; } // Plánované množství - editovatelné v Draft/Planned stavech

    public string? LotNumber { get; set; } // Šarže

    public DateOnly? ExpirationDate { get; set; } // Expirace

    public decimal? ActualQuantity { get; set; } // Skutečné množství při výrobě
}