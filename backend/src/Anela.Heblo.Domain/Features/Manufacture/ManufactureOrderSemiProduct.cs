namespace Anela.Heblo.Domain.Features.Manufacture;

public class ManufactureOrderSemiProduct
{
    public int Id { get; set; }
    public int ManufactureOrderId { get; set; }
    public string ProductCode { get; set; } = null!;
    public string ProductName { get; set; } = null!;
    public decimal PlannedQuantity { get; set; } // Z batch calculatoru
    public decimal ActualQuantity { get; set; } // Upravené množství při výrobě

    // Navigation property
    public ManufactureOrder ManufactureOrder { get; set; } = null!;

    // Ingredients se načtou dynamicky z ManufactureTemplate při dokončování
}