using Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseStockAnalysis;

namespace Anela.Heblo.Application.Features.Purchase.Services;

/// <summary>
/// Service responsible for calculating stock severity levels based on current stock and configured thresholds.
/// </summary>
public class StockSeverityCalculator : IStockSeverityCalculator
{
    /// <summary>
    /// Determines the severity level of stock based on available stock and configured thresholds.
    /// </summary>
    /// <param name="availableStock">Current available stock amount</param>
    /// <param name="minStock">Configured minimum stock level</param>
    /// <param name="optimalStock">Calculated optimal stock level</param>
    /// <param name="isMinConfigured">Whether minimum stock level is configured</param>
    /// <param name="isOptimalConfigured">Whether optimal stock level is configured</param>
    /// <returns>Stock severity level</returns>
    public StockSeverity DetermineStockSeverity(
        double availableStock,
        double minStock,
        double optimalStock,
        bool isMinConfigured,
        bool isOptimalConfigured)
    {
        // Not configured - missing min/optimal stock settings
        if (!isMinConfigured && !isOptimalConfigured)
        {
            return StockSeverity.NotConfigured;
        }

        // Critical - below minimum OR below 20% of optimal
        if ((isMinConfigured && availableStock < minStock) ||
            (isOptimalConfigured && availableStock < optimalStock * CriticalOptimalThresholdMultiplier))
        {
            return StockSeverity.Critical;
        }

        // Low - between 20-70% of optimal stock
        if (isOptimalConfigured && availableStock >= optimalStock * CriticalOptimalThresholdMultiplier &&
            availableStock < optimalStock * LowOptimalThresholdMultiplier)
        {
            return StockSeverity.Low;
        }

        // Overstocked - more than 150% of optimal stock
        if (isOptimalConfigured && availableStock > optimalStock * OverstockedThresholdMultiplier)
        {
            return StockSeverity.Overstocked;
        }

        // Optimal - 70-150% of optimal stock (everything else)
        return StockSeverity.Optimal;
    }

    /// <summary>
    /// Threshold multiplier for critical stock level based on optimal stock (20% of optimal)
    /// </summary>
    private const double CriticalOptimalThresholdMultiplier = 0.2;

    /// <summary>
    /// Threshold multiplier for low stock level based on optimal stock (70% of optimal)
    /// </summary>
    private const double LowOptimalThresholdMultiplier = 0.7;

    /// <summary>
    /// Threshold multiplier for overstocked level (150% of optimal)
    /// </summary>
    private const double OverstockedThresholdMultiplier = 1.5;
}