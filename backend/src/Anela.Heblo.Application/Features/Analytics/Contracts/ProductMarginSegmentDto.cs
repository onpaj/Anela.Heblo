namespace Anela.Heblo.Application.Features.Analytics.Contracts;

public class ProductMarginSegmentDto
{
    public string GroupKey { get; set; } = string.Empty; // Group identifier (product code, family, type)
    public string DisplayName { get; set; } = string.Empty; // Display name for the group
    public decimal MarginContribution { get; set; } // Total margin for this group in this month
    public decimal Percentage { get; set; } // Percentage of monthly total margin
    public string ColorCode { get; set; } = string.Empty; // Hex color for consistency
    public bool IsOther { get; set; } = false; // True for "Other" category

    // Tooltip detail information (aggregated for group)
    public decimal AverageMarginPerPiece { get; set; } // Average margin per piece in group
    public int UnitsSold { get; set; } // Total units sold in this month for group
    public decimal AverageSellingPriceWithoutVat { get; set; } // Average selling price in group
    public decimal AverageMaterialCosts { get; set; } // Average material costs in group
    public decimal AverageLaborCosts { get; set; } // Average labor costs in group
    public int ProductCount { get; set; } // Number of products in this group
    
    // Keep for backward compatibility
    public string ProductCode => GroupKey;
    public string ProductName => DisplayName;
    public decimal MarginPerPiece => AverageMarginPerPiece;
    public decimal SellingPriceWithoutVat => AverageSellingPriceWithoutVat;
    public decimal MaterialCosts => AverageMaterialCosts;
    public decimal LaborCosts => AverageLaborCosts;
}