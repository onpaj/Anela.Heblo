namespace Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrder;

public class UpdateManufactureOrderSemiProductDto
{
    public int Id { get; set; }
    public string ProductCode { get; set; } = null!;
    public string ProductName { get; set; } = null!;
    public decimal PlannedQuantity { get; set; }
    public decimal ActualQuantity { get; set; }
    public string? LotNumber { get; set; } // Šarže
    public DateOnly? ExpirationDate { get; set; } // Expirace
}