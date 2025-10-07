namespace Anela.Heblo.Application.Features.Catalog.Contracts;

public class ProductMarginDto
{
    // Existing properties
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal? PriceWithoutVat { get; set; }
    public decimal? PurchasePrice { get; set; }
    public decimal? AverageMaterialCost { get; set; } // Average MaterialCost from ManufactureCostHistory (excluding zero values)
    public decimal? AverageHandlingCost { get; set; } // Average HandlingCost from ManufactureCostHistory (excluding zero values)
    public decimal? AverageSalesCost { get; set; }
    public decimal? AverageOverheadCost { get; set; }

    public double ManufactureDifficulty { get; set; }
    public decimal MarginPercentage { get; set; } // Direct from CatalogAggregate.MarginPercentage
    public decimal MarginAmount { get; set; } // Direct from CatalogAggregate.MarginAmount
    public bool PriceWithoutVatIsFromEshop { get; set; } = false;

    // Current month margin levels
    public decimal M0Percentage { get; set; }  // Direct material margin %
    public decimal M0Amount { get; set; }      // Direct material margin amount

    public decimal M1Percentage { get; set; }  // Manufacturing margin %
    public decimal M1Amount { get; set; }      // Manufacturing margin amount

    public decimal M2Percentage { get; set; }  // Sales & marketing margin %
    public decimal M2Amount { get; set; }      // Sales & marketing margin amount

    public decimal M3Percentage { get; set; }  // Net profitability %
    public decimal M3Amount { get; set; }      // Net profitability amount

    // Cost components for tooltips (current month)
    public decimal? MaterialCost { get; set; }
    public decimal? ManufacturingCost { get; set; }
    public decimal? SalesCost { get; set; }
    public decimal? OverheadCost { get; set; }

    // Historical data for charts (13 months)
    public List<MonthlyMarginDto> MonthlyHistory { get; set; } = new();
}

public class MonthlyMarginDto
{
    public DateTime Month { get; set; }
    public decimal M0Percentage { get; set; }
    public decimal M1Percentage { get; set; }
    public decimal M2Percentage { get; set; }
    public decimal M3Percentage { get; set; }
    public decimal MaterialCost { get; set; }
    public decimal ManufacturingCost { get; set; }
    public decimal SalesCost { get; set; }
    public decimal TotalCosts { get; set; }
}