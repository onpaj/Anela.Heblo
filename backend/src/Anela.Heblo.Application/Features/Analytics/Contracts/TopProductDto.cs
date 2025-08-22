namespace Anela.Heblo.Application.Features.Analytics.Contracts;

public class TopProductDto
{
    public string GroupKey { get; set; } = string.Empty; // Product code, family, or type key
    public string DisplayName { get; set; } = string.Empty; // Display name for the group
    public decimal TotalMargin { get; set; } // Total margin across entire time period
    public string ColorCode { get; set; } = string.Empty;
    public int Rank { get; set; }
    
    // Keep for backward compatibility
    public string ProductCode => GroupKey;
    public string ProductName => DisplayName;
}