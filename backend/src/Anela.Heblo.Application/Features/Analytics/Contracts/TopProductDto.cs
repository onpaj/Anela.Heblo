namespace Anela.Heblo.Application.Features.Analytics.Contracts;

public class TopProductDto
{
    public string GroupKey { get; set; } = string.Empty; // Product code, family, or type key
    public string DisplayName { get; set; } = string.Empty; // Display name for the group
    public decimal TotalMargin { get; set; } // Total margin across entire time period
    public string ColorCode { get; set; } = string.Empty;
    public int Rank { get; set; }
    
    // M0-M3 margin levels - amounts (for sorting)
    public decimal M0Amount { get; set; }
    public decimal M1Amount { get; set; }
    public decimal M2Amount { get; set; }
    public decimal M3Amount { get; set; }
    
    // M0-M3 margin levels - percentages (for sorting)
    public decimal M0Percentage { get; set; }
    public decimal M1Percentage { get; set; }
    public decimal M2Percentage { get; set; }
    public decimal M3Percentage { get; set; }
    
    // Pricing (for sorting)
    public decimal SellingPrice { get; set; }
    public decimal PurchasePrice { get; set; }

    // Keep for backward compatibility
    public string ProductCode => GroupKey;
    public string ProductName => DisplayName;
}