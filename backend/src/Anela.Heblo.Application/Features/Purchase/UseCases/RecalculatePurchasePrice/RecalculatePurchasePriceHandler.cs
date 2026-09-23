using Anela.Heblo.Application.Features.Purchase.Contracts;
using Anela.Heblo.Application.Shared;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Purchase.UseCases.RecalculatePurchasePrice;

/// <summary>
/// Single product: recalculates that item's BoM purchase price in Flexi.
///
/// RecalculateAll (nightly job) runs three phases in strict order:
/// 1. materials and goods: ceník nakupCena := average stock price (prumCena);
/// 2. semi-product BoMs; 3. product and set BoMs.
/// Flexi's roll-up (prepocti-nakupni-cenu) sums each component's STORED nakupCena, so each
/// level must be current before the level above it is recalculated.
/// </summary>
public class RecalculatePurchasePriceHandler : IRequestHandler<RecalculatePurchasePriceRequest, RecalculatePurchasePriceResponse>
{
    /// <summary>Differences below this are rounding noise, not a price change worth writing to the ERP.</summary>
    internal const decimal PurchasePriceTolerance = 0.0001m;

    private const int MaxLoggedSkippedCodes = 10;

    private readonly IMaterialCatalogService _materialCatalog;
    private readonly IPurchasePriceRecalculationService _priceRecalculationService;
    private readonly IPurchasePriceSyncSource _priceSyncSource;
    private readonly ILogger<RecalculatePurchasePriceHandler> _logger;

    public RecalculatePurchasePriceHandler(
        IMaterialCatalogService materialCatalog,
        IPurchasePriceRecalculationService priceRecalculationService,
        IPurchasePriceSyncSource priceSyncSource,
        ILogger<RecalculatePurchasePriceHandler> logger)
    {
        _materialCatalog = materialCatalog;
        _priceRecalculationService = priceRecalculationService;
        _priceSyncSource = priceSyncSource;
        _logger = logger;
    }

    public async Task<RecalculatePurchasePriceResponse> Handle(RecalculatePurchasePriceRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting price recalculation - ProductCode: {ProductCode}, RecalculateAll: {RecalculateAll}",
            request.ProductCode, request.RecalculateAll);

        if (string.IsNullOrEmpty(request.ProductCode) && !request.RecalculateAll)
        {
            _logger.LogWarning("Invalid request: Either ProductCode must be specified or RecalculateAll must be true");
            return new RecalculatePurchasePriceResponse(ErrorCodes.InvalidValue, new Dictionary<string, string> { { "Message", "Either ProductCode must be specified or RecalculateAll must be true" } });
        }

        return request.RecalculateAll
            ? await RecalculateAllAsync(cancellationToken)
            : await RecalculateSingleAsync(request.ProductCode!, cancellationToken);
    }

    private async Task<RecalculatePurchasePriceResponse> RecalculateSingleAsync(string productCode, CancellationToken cancellationToken)
    {
        var product = await _materialCatalog.GetByIdAsync(productCode, cancellationToken);
        if (product == null)
        {
            _logger.LogWarning("Product with code '{ProductCode}' not found", productCode);
            return new RecalculatePurchasePriceResponse(ErrorCodes.CatalogItemNotFound, new Dictionary<string, string> { { "ProductCode", productCode } });
        }

        if (!product.HasBoM || !product.BoMId.HasValue)
        {
            _logger.LogWarning("Product '{ProductCode}' does not have BoM", productCode);
            return new RecalculatePurchasePriceResponse(ErrorCodes.InvalidValue, new Dictionary<string, string> { { "ProductCode", productCode }, { "Message", $"Product {productCode} does not have BoM" } });
        }

        var response = new RecalculatePurchasePriceResponse { TotalCount = 1 };
        await RecalculateBomsAsync(
            new[] { new MaterialBomReference { ProductCode = product.ProductCode, BoMId = product.BoMId.Value } },
            response, cancellationToken);

        LogCompletion(response);
        return response;
    }

    private async Task<RecalculatePurchasePriceResponse> RecalculateAllAsync(CancellationToken cancellationToken)
    {
        var response = new RecalculatePurchasePriceResponse();

        // Phase 1. A load failure propagates: no BoM is recalculated on partial data.
        response.PriceSync = await SyncStockPricesAsync(cancellationToken);

        var boms = await _materialCatalog.GetMaterialsWithBomAsync(cancellationToken);
        response.TotalCount = boms.Count;

        response.SemiProducts = await RecalculateBomsAsync(boms.Where(b => b.IsSemiProduct).ToList(), response, cancellationToken);
        _logger.LogInformation("Purchase price phase 2 (semi-products): {Succeeded} succeeded, {Failed} failed",
            response.SemiProducts.Succeeded, response.SemiProducts.Failed);

        response.Products = await RecalculateBomsAsync(boms.Where(b => !b.IsSemiProduct).ToList(), response, cancellationToken);
        _logger.LogInformation("Purchase price phase 3 (products and sets): {Succeeded} succeeded, {Failed} failed",
            response.Products.Succeeded, response.Products.Failed);

        LogCompletion(response);
        return response;
    }

    private async Task<PurchasePriceSyncSummary> SyncStockPricesAsync(CancellationToken cancellationToken)
    {
        var candidates = await _priceSyncSource.GetCandidatesAsync(cancellationToken);
        var summary = new PurchasePriceSyncSummary { Candidates = candidates.Count };
        var skippedCodes = new List<string>();

        foreach (var candidate in candidates)
        {
            if (candidate.StockPrice is not > 0m)
            {
                summary.SkippedNoStockPrice++;
                skippedCodes.Add(candidate.ProductCode);
                continue;
            }

            var stockPrice = candidate.StockPrice.Value;
            if (Math.Abs(stockPrice - candidate.CurrentPurchasePrice) < PurchasePriceTolerance)
            {
                summary.Unchanged++;
                continue;
            }

            await WritePurchasePriceAsync(candidate, stockPrice, summary, cancellationToken);
        }

        if (skippedCodes.Count > 0)
        {
            _logger.LogWarning(
                "Purchase price sync skipped {Count} items without a usable stock price (e.g. {ProductCodes})",
                skippedCodes.Count, string.Join(", ", skippedCodes.Take(MaxLoggedSkippedCodes)));
        }

        const string phase1Summary =
            "Purchase price phase 1 (materials and goods): {Candidates} candidates, {Written} written, {Unchanged} unchanged, {Skipped} skipped, {Failed} failed";
        if (summary.Failed > 0)
        {
            _logger.LogWarning(phase1Summary,
                summary.Candidates, summary.Written, summary.Unchanged, summary.SkippedNoStockPrice, summary.Failed);
        }
        else
        {
            _logger.LogInformation(phase1Summary,
                summary.Candidates, summary.Written, summary.Unchanged, summary.SkippedNoStockPrice, summary.Failed);
        }

        return summary;
    }

    private async Task WritePurchasePriceAsync(
        PurchasePriceSyncCandidate candidate, decimal stockPrice, PurchasePriceSyncSummary summary, CancellationToken cancellationToken)
    {
        try
        {
            await _priceRecalculationService.SetPurchasePriceAsync(candidate.ErpItemId, stockPrice, cancellationToken);
            summary.Written++;
            _logger.LogInformation(
                "Purchase price sync: {ProductCode} ({ProductType}) nakupCena {OldPrice} -> {NewPrice} (prumCena)",
                candidate.ProductCode, candidate.ProductType, candidate.CurrentPurchasePrice, stockPrice);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            summary.Failed++;
            _logger.LogError(ex, "Purchase price sync failed for {ProductCode} (ceník {ErpItemId})",
                candidate.ProductCode, candidate.ErpItemId);
        }
    }

    private async Task<BomRecalculationSummary> RecalculateBomsAsync(
        IReadOnlyList<MaterialBomReference> boms, RecalculatePurchasePriceResponse response, CancellationToken cancellationToken)
    {
        var summary = new BomRecalculationSummary();

        foreach (var bom in boms)
        {
            try
            {
                await _priceRecalculationService.RecalculatePurchasePriceAsync(bom.BoMId, cancellationToken);
                response.ProcessedProducts.Add(new ProductRecalculationResult { ProductCode = bom.ProductCode, Success = true });
                response.SuccessCount++;
                summary.Succeeded++;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                response.ProcessedProducts.Add(new ProductRecalculationResult
                {
                    ProductCode = bom.ProductCode,
                    Success = false,
                    ErrorCode = ErrorCodes.Exception,
                    Params = new Dictionary<string, string>
                    {
                        { "message", ex.Message },
                        { "exceptionType", ex.GetType().Name }
                    }
                });
                response.FailedCount++;
                summary.Failed++;
                _logger.LogError(ex, "Failed to recalculate price for product {ProductCode}: {ErrorMessage}",
                    bom.ProductCode, ex.Message);
            }
        }

        return summary;
    }

    private void LogCompletion(RecalculatePurchasePriceResponse response) =>
        _logger.LogInformation("Price recalculation completed - Success: {SuccessCount}, Failed: {FailedCount}, Total: {TotalCount}",
            response.SuccessCount, response.FailedCount, response.TotalCount);
}
