using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Analytics.UseCases.GetMarginReport;

public class GetMarginReportResponse : BaseResponse
{
    public DateTime ReportPeriodStart { get; set; }
    public DateTime ReportPeriodEnd { get; set; }
    public decimal TotalMargin { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal TotalCost { get; set; }
    public decimal AverageMarginPercentage { get; set; }
    public int TotalProductsAnalyzed { get; set; }
    public int TotalUnitsSold { get; set; }
    public List<ProductMarginSummary> ProductSummaries { get; set; } = new();
    public List<CategoryMarginSummary> CategorySummaries { get; set; } = new();

    public class ProductMarginSummary
    {
        public string ProductId { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public decimal MarginAmount { get; set; }
        
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
        
        public decimal MarginPercentage { get; set; }
        public decimal Revenue { get; set; }
        public decimal Cost { get; set; }
        public int UnitsSold { get; set; }
    }

    public class CategoryMarginSummary
    {
        public string Category { get; set; } = string.Empty;
        public decimal TotalMargin { get; set; }
        public decimal TotalRevenue { get; set; }
        public decimal AverageMarginPercentage { get; set; }
        public int ProductCount { get; set; }
        public int TotalUnitsSold { get; set; }
    }
}