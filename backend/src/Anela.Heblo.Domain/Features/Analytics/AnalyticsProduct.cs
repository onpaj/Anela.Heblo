using Anela.Heblo.Domain.Features.Catalog;

namespace Anela.Heblo.Domain.Features.Analytics;

/// <summary>
/// 🔒 PERFORMANCE FIX: Lightweight product model for analytics
/// Contains only properties needed for margin calculations to reduce memory usage
/// </summary>
public class AnalyticsProduct
{
    public required string ProductCode { get; init; }
    public required string ProductName { get; init; }
    public required ProductType Type { get; init; }
    public string? ProductFamily { get; init; }
    public string? ProductCategory { get; init; }
    public required decimal MarginAmount { get; init; }

    // M0-M2 margin levels - amounts
    public decimal M0Amount { get; init; } // Material + manufacturing margin
    public decimal M1Amount { get; init; } // M0 + additional manufacturing margin
    public decimal M2Amount { get; init; } // M1 + sales margin

    // M0-M2 margin levels - percentages
    public decimal M0Percentage { get; init; }
    public decimal M1Percentage { get; init; }
    public decimal M2Percentage { get; init; }

    // Pricing
    public decimal SellingPrice { get; init; }
    public decimal? EshopPriceWithoutVat { get; init; }
    public decimal PurchasePrice { get; init; } // Latest purchase price

    // Cost components
    public decimal MaterialCost { get; init; }
    public decimal HandlingCost { get; init; }

    /// <summary>
    /// Sales history only for the requested period (filtered by repository)
    /// </summary>
    public required List<SalesDataPoint> SalesHistory { get; init; }
}

/// <summary>
/// Lightweight sales data point for analytics
/// </summary>
public class SalesDataPoint
{
    public required DateTime Date { get; init; }
    public required double AmountB2B { get; init; }
    public required double AmountB2C { get; init; }
}

/// <summary>
/// Value object for date range queries
/// </summary>
public record DateRange(DateTime FromDate, DateTime ToDate);

/// <summary>
/// Result object for margin calculations
/// </summary>
public class MarginCalculationResult
{
    public required Dictionary<string, decimal> GroupTotals { get; init; }
    public required Dictionary<string, List<AnalyticsProduct>> GroupProducts { get; init; }
    public required decimal TotalMargin { get; init; }
}