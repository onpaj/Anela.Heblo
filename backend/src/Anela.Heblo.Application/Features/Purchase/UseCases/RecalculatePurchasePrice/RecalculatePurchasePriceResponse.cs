using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Purchase.UseCases.RecalculatePurchasePrice;

public class RecalculatePurchasePriceResponse : BaseResponse
{
    /// <summary>
    /// Number of products successfully recalculated.
    /// </summary>
    public int SuccessCount { get; set; }

    /// <summary>
    /// Number of products that failed recalculation.
    /// </summary>
    public int FailedCount { get; set; }

    /// <summary>
    /// Total number of products processed.
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// List of all products that were processed with their recalculation status.
    /// </summary>
    public List<ProductRecalculationResult> ProcessedProducts { get; set; } = new();

    /// <summary>
    /// Whether the overall operation was successful (all products recalculated successfully).
    /// </summary>
    public bool IsSuccess => FailedCount == 0 && TotalCount > 0;

    /// <summary>
    /// Summary message of the operation result.
    /// </summary>
    public string Message => TotalCount switch
    {
        0 => "No products found to recalculate",
        1 when IsSuccess => $"Successfully recalculated price for 1 product",
        1 when !IsSuccess => $"Failed to recalculate price for 1 product",
        _ when IsSuccess => $"Successfully recalculated prices for all {TotalCount} products",
        _ => $"Recalculated {SuccessCount} of {TotalCount} products ({FailedCount} failed)"
    };
}

public class ProductRecalculationResult
{
    /// <summary>
    /// Product code that was processed.
    /// </summary>
    public string ProductCode { get; set; } = string.Empty;

    /// <summary>
    /// Whether the recalculation was successful.
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// Error message if the recalculation failed.
    /// </summary>
    public string? ErrorMessage { get; set; }
}

