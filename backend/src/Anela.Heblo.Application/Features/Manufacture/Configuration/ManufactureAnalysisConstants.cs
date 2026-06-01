namespace Anela.Heblo.Application.Features.Manufacture.Configuration;

/// <summary>
/// Constants for manufacture analysis calculations
/// </summary>
public static class ManufactureAnalysisConstants
{
    /// <summary>
    /// Special value indicating unlimited analysis period
    /// </summary>
    public const int UnlimitedAnalysisPeriod = 999;

    /// <summary>
    /// Minimum valid consumption rate to avoid division by zero
    /// </summary>
    public const decimal MinimumConsumptionRate = 0.001m;

    /// <summary>
    /// Maximum reasonable days of stock for calculations (~100 years)
    /// </summary>
    public const int MaxReasonableDaysOfStock = 36500;
}