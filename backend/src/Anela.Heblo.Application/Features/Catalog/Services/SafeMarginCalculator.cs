using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Services;

public class SafeMarginCalculator
{
    private readonly ILogger<SafeMarginCalculator> _logger;

    public SafeMarginCalculator(ILogger<SafeMarginCalculator> logger)
    {
        _logger = logger;
    }

    public MarginCalculationResult CalculateMargin(decimal? sellingPrice, decimal? cost)
    {
        try
        {
            // Handle null inputs
            if (!sellingPrice.HasValue || !cost.HasValue)
            {
                return MarginCalculationResult.Invalid("Missing price or cost data");
            }

            var sellingPriceValue = sellingPrice.Value;
            var costValue = cost.Value;

            // Validate inputs
            if (sellingPriceValue < 0 || costValue < 0)
            {
                return MarginCalculationResult.Invalid("Negative prices or costs are not allowed");
            }

            if (sellingPriceValue == 0)
            {
                return MarginCalculationResult.Invalid("Cannot calculate margin with zero selling price");
            }

            // Calculate margin safely
            var margin = ((sellingPriceValue - costValue) / sellingPriceValue) * 100;

            // For decimal, we don't need to check for infinity/NaN as division by zero would throw
            // The validation above already ensures sellingPriceValue != 0
            return MarginCalculationResult.Success(Math.Round(margin, 2));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating margin for price {Price}, cost {Cost}", sellingPrice, cost);
            return MarginCalculationResult.Error("Margin calculation failed", ex);
        }
    }
}

public class MarginCalculationResult
{
    public bool IsSuccess { get; init; }
    public decimal? Margin { get; init; }
    public string? ErrorMessage { get; init; }
    public Exception? Exception { get; init; }

    public static MarginCalculationResult Success(decimal margin) =>
        new() { IsSuccess = true, Margin = margin };

    public static MarginCalculationResult Invalid(string message) =>
        new() { IsSuccess = false, ErrorMessage = message };

    public static MarginCalculationResult Error(string message, Exception ex) =>
        new() { IsSuccess = false, ErrorMessage = message, Exception = ex };
}