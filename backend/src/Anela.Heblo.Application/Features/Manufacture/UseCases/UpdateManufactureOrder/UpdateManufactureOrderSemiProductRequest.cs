namespace Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrder;

public class UpdateManufactureOrderSemiProductRequest
{
    public string? LotNumber { get; set; } // Šarže

    public DateOnly? ExpirationDate { get; set; } // Expirace
}