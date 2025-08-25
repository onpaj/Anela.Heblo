namespace Anela.Heblo.Application.Features.Manufacture.Configuration;

/// <summary>
/// Configuration options for manufacture stock analysis calculations
/// </summary>
public class ManufactureAnalysisOptions
{
    /// <summary>
    /// Default number of months to analyze consumption history
    /// </summary>
    public int DefaultMonthsBack { get; set; } = 12;
    
    /// <summary>
    /// Maximum allowed months for consumption analysis
    /// </summary>
    public int MaxMonthsBack { get; set; } = 60;
    
    /// <summary>
    /// Number of days to consider for active production status
    /// </summary>
    public int ProductionActivityDays { get; set; } = 30;
    
    /// <summary>
    /// Stock level multiplier for Critical severity (days of stock ≤ lead time × multiplier)
    /// </summary>
    public decimal CriticalStockMultiplier { get; set; } = 1.0m;
    
    /// <summary>
    /// Stock level multiplier for High severity (days of stock ≤ lead time × multiplier)
    /// </summary>
    public decimal HighStockMultiplier { get; set; } = 1.5m;
    
    /// <summary>
    /// Stock level multiplier for Medium severity (days of stock ≤ lead time × multiplier)
    /// </summary>
    public decimal MediumStockMultiplier { get; set; } = 2.0m;
    
    /// <summary>
    /// Value representing infinite stock when consumption rate is zero
    /// </summary>
    public int InfiniteStockIndicator { get; set; } = 999999;
}