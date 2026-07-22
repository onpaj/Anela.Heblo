namespace Anela.Heblo.Application.Features.Purchase.Services;

/// <summary>
/// Interface for calculating stock analysis metrics such as efficiency and recommended order quantity.
/// </summary>
public interface IStockAnalysisCalculator
{
    /// <summary>
    /// Calculates the stock efficiency percentage based on available stock relative to optimal or minimum stock.
    /// </summary>
    /// <param name="availableStock">Current available stock amount</param>
    /// <param name="minStock">Configured minimum stock level</param>
    /// <param name="optimalStock">Calculated optimal stock level</param>
    /// <returns>Stock efficiency percentage</returns>
    double CalculateStockEfficiency(double availableStock, double minStock, double optimalStock);

    /// <summary>
    /// Calculates the recommended order quantity needed to reach the target stock level.
    /// </summary>
    /// <param name="availableStock">Current available stock amount</param>
    /// <param name="optimalStock">Calculated optimal stock level</param>
    /// <param name="minStock">Configured minimum stock level</param>
    /// <param name="moq">Minimum order quantity as configured for the material</param>
    /// <returns>Recommended order quantity, or null if no order is needed</returns>
    double? CalculateRecommendedOrderQuantity(double availableStock, double optimalStock, double minStock, string moq);
}
