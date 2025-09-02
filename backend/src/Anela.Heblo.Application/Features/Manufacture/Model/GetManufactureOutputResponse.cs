namespace Anela.Heblo.Application.Features.Manufacture.Model;

public class GetManufactureOutputResponse
{
    public List<ManufactureOutputMonth> Months { get; set; } = new();
}

public class ManufactureOutputMonth
{
    public string Month { get; set; } = string.Empty; // Format: "2024-01"
    public double TotalOutput { get; set; } // Weighted sum of all products
    public List<ProductContribution> Products { get; set; } = new();
    public List<ProductionDetail> ProductionDetails { get; set; } = new();
}

public class ProductContribution
{
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public double Quantity { get; set; } // How many units were manufactured
    public double Difficulty { get; set; } // ManufactureDifficulty from catalog
    public double WeightedValue { get; set; } // Quantity * Difficulty
}

public class ProductionDetail
{
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public double Amount { get; set; }
    public decimal PricePerPiece { get; set; }
    public decimal PriceTotal { get; set; }
    public string DocumentNumber { get; set; } = string.Empty;
}