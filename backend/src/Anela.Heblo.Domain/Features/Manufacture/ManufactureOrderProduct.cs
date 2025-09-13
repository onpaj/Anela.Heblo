namespace Anela.Heblo.Domain.Features.Manufacture;

public class ManufactureOrderProduct
{
    public int Id { get; set; }
    public int ManufactureOrderId { get; set; }
    public string ProductCode { get; set; } = null!;
    public string ProductName { get; set; } = null!;
    public string SemiProductCode { get; set; } = null!; // Odkaz na semi-product
    public decimal PlannedQuantity { get; set; }
    public decimal ActualQuantity { get; set; }
    public string? LotNumber { get; set; } // Šarže pro finální produkty - předvyplní se z meziproduktu, uživatel může upravit
    public DateOnly? ExpirationDate { get; set; } // Expirace pro finální produkty - předvyplní se z meziproduktu, uživatel může upravit

    // Navigation property
    public ManufactureOrder ManufactureOrder { get; set; } = null!;
}