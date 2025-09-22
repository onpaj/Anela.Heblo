namespace Anela.Heblo.Domain.Features.Manufacture;

public class ManufactureOrderSemiProduct
{
    public int Id { get; set; }
    public int ManufactureOrderId { get; set; }
    public string ProductCode { get; set; } = null!;
    public string ProductName { get; set; } = null!;
    public decimal PlannedQuantity { get; set; } // Z batch calculatoru
    public decimal ActualQuantity { get; set; } // Upravené množství při výrobě
    public decimal BatchMultiplier { get; set; } // Multiplikátor z batch calculatoru (ScaleFactor)
    public string? LotNumber { get; set; } // Šarže pro meziprodukty - zadává uživatel při úpravě množství
    public DateOnly? ExpirationDate { get; set; } // Expirace pro meziprodukty - zadává uživatel při úpravě množství

    // Navigation property
    public ManufactureOrder ManufactureOrder { get; set; } = null!;
    public int ExpirationMonths { get; set; }

    // Ingredients se načtou dynamicky z ManufactureTemplate při dokončování
}