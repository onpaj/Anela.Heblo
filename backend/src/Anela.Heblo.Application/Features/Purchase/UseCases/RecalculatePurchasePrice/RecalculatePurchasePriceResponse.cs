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
    /// Phase 1 of a RecalculateAll run: materials and goods synced to their average stock price.
    /// </summary>
    public PurchasePriceSyncSummary PriceSync { get; set; } = new();

    /// <summary>Phase 2 of a RecalculateAll run: semi-product BoM recalculations.</summary>
    public BomRecalculationSummary SemiProducts { get; set; } = new();

    /// <summary>Phase 3 of a RecalculateAll run: product and set BoM recalculations.</summary>
    public BomRecalculationSummary Products { get; set; } = new();

    /// <summary>
    /// Whether the overall operation was successful (all purchase price writes and all
    /// products recalculated successfully).
    /// </summary>
    public bool IsSuccess => FailedCount == 0 && PriceSync.Failed == 0 && TotalCount > 0;

    /// <summary>
    /// Summary message of the operation result.
    /// </summary>
    public string Message => PriceSync.Failed > 0
        ? $"{BomMessage}; {PriceSync.Failed} purchase price writes failed"
        : BomMessage;

    private string BomMessage => TotalCount switch
    {
        0 => "No products found to recalculate",
        1 when FailedCount == 0 => $"Successfully recalculated price for 1 product",
        1 => $"Failed to recalculate price for 1 product",
        _ when FailedCount == 0 => $"Successfully recalculated prices for all {TotalCount} products",
        _ => $"Recalculated {SuccessCount} of {TotalCount} products ({FailedCount} failed)"
    };

    public RecalculatePurchasePriceResponse() : base() { }
    public RecalculatePurchasePriceResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }
}

public class ProductRecalculationResult : BaseResponse
{
    /// <summary>
    /// Product code that was processed.
    /// </summary>
    public string ProductCode { get; set; } = string.Empty;
}

public class PurchasePriceSyncSummary
{
    public int Candidates { get; set; }
    public int Written { get; set; }
    public int Unchanged { get; set; }
    public int SkippedNoStockPrice { get; set; }
    public int Failed { get; set; }
}

public class BomRecalculationSummary
{
    public int Succeeded { get; set; }
    public int Failed { get; set; }
}

