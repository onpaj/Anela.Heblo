namespace Anela.Heblo.Application.Features.Analytics.Services;

/// <summary>
/// Helper class for aggregated margin data calculation
/// </summary>
public class GroupMarginData
{
    public decimal M0Amount { get; set; }
    public decimal M1Amount { get; set; }
    public decimal M2Amount { get; set; }
    public decimal M0Percentage { get; set; }
    public decimal M1Percentage { get; set; }
    public decimal M2Percentage { get; set; }
    public decimal SellingPrice { get; set; }
    public decimal PurchasePrice { get; set; }
}
