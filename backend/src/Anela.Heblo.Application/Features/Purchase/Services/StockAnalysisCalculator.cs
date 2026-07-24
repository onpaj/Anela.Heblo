namespace Anela.Heblo.Application.Features.Purchase.Services;

/// <summary>
/// Service responsible for calculating stock analysis metrics such as efficiency and recommended order quantity.
/// </summary>
public class StockAnalysisCalculator : IStockAnalysisCalculator
{
    /// <summary>
    /// Calculates the stock efficiency percentage based on available stock relative to optimal or minimum stock.
    /// </summary>
    /// <param name="availableStock">Current available stock amount</param>
    /// <param name="minStock">Configured minimum stock level</param>
    /// <param name="optimalStock">Calculated optimal stock level</param>
    /// <returns>Stock efficiency percentage</returns>
    public double CalculateStockEfficiency(double availableStock, double minStock, double optimalStock)
    {
        if (optimalStock <= 0)
        {
            return minStock > 0 ? (availableStock / minStock) * 100 : 0;
        }

        return (availableStock / optimalStock) * 100;
    }

    /// <summary>
    /// Calculates the recommended order quantity needed to reach the target stock level.
    /// </summary>
    /// <param name="availableStock">Current available stock amount</param>
    /// <param name="optimalStock">Calculated optimal stock level</param>
    /// <param name="minStock">Configured minimum stock level</param>
    /// <param name="moq">Minimum order quantity as configured for the material</param>
    /// <returns>Recommended order quantity, or null if no order is needed</returns>
    public double? CalculateRecommendedOrderQuantity(double availableStock, double optimalStock, double minStock, string moq)
    {
        if (optimalStock <= 0 && minStock <= 0)
        {
            return null;
        }

        var targetStock = optimalStock > 0 ? optimalStock : minStock * 2;
        var needed = targetStock - availableStock;

        if (needed <= 0)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(moq) && double.TryParse(moq, out var minOrderQty))
        {
            return Math.Max(needed, minOrderQty);
        }

        return needed;
    }
}
