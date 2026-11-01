using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Domain.Features.DataQuality;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.DataQuality.Services;

public class ProductPairingDqtComparer : IDriftDqtComparer
{
    private readonly IDqtEshopStockSource _eshopStockSource;
    private readonly IDqtErpStockSource _erpStockSource;
    private readonly IDqtResilienceService _resilienceService;
    private readonly ILogger<ProductPairingDqtComparer> _logger;

    public DqtTestType TestType => DqtTestType.ProductPairing;

    public ProductPairingDqtComparer(
        IDqtEshopStockSource eshopStockSource,
        IDqtErpStockSource erpStockSource,
        IDqtResilienceService resilienceService,
        ILogger<ProductPairingDqtComparer> logger)
    {
        _eshopStockSource = eshopStockSource;
        _erpStockSource = erpStockSource;
        _resilienceService = resilienceService;
        _logger = logger;
    }

    public async Task<DriftComparisonResult> CompareAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        // Date range is intentionally unused — product pairing is a current-state snapshot
        IReadOnlyList<DqtEshopStockItem> eshopProducts;
        try
        {
            eshopProducts = await _resilienceService.ExecuteWithResilienceAsync(
                async cancellationToken => await _eshopStockSource.ListAsync(cancellationToken),
                "ProductPairingDqtComparer.EshopList",
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "ProductPairingDqtComparer failed to fetch eshop products after resilience exhaustion. Operation={Operation} ExceptionType={ExceptionType}",
                "ProductPairingDqtComparer.EshopList",
                ex.GetType().Name);
            throw;
        }

        IReadOnlyList<DqtErpStockItem> erpProducts;
        try
        {
            erpProducts = await _resilienceService.ExecuteWithResilienceAsync(
                async cancellationToken => await _erpStockSource.ListAsync(cancellationToken),
                "ProductPairingDqtComparer.ErpList",
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "ProductPairingDqtComparer failed to fetch ERP products after resilience exhaustion. Operation={Operation} ExceptionType={ExceptionType}",
                "ProductPairingDqtComparer.ErpList",
                ex.GetType().Name);
            throw;
        }

        var sellableErpProducts = erpProducts.Where(p => p.IsSellable).ToList();

        var erpCodeSet = sellableErpProducts
            .Select(p => p.ProductCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var shoptetCodes = eshopProducts
            .Select(p => p.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var mismatches = new List<DriftMismatch>();

        // Check A: each Shoptet product must exist in ERP (matched by code)
        foreach (var eshopProduct in eshopProducts)
        {
            if (erpCodeSet.Contains(eshopProduct.Code))
                continue;

            mismatches.Add(new DriftMismatch
            {
                EntityKey = eshopProduct.Code,
                MismatchCode = (int)ProductPairingMismatch.MissingInErp,
                ShoptetValue = eshopProduct.Name,
                HebloValue = null,
                Details = $"Shoptet product '{eshopProduct.Code}' not found in ERP"
            });
        }

        // Check B: each sellable ERP product must appear in Shoptet
        foreach (var erpProduct in sellableErpProducts)
        {
            if (shoptetCodes.Contains(erpProduct.ProductCode))
                continue;

            mismatches.Add(new DriftMismatch
            {
                EntityKey = erpProduct.ProductCode,
                MismatchCode = (int)ProductPairingMismatch.MissingInShoptet,
                HebloValue = erpProduct.ProductName,
                ShoptetValue = null,
                Details = $"Sellable ERP product '{erpProduct.ProductCode}' not in Shoptet catalog"
            });
        }

        var totalChecked = shoptetCodes
            .Union(erpCodeSet, StringComparer.OrdinalIgnoreCase)
            .Count();

        return new DriftComparisonResult { Mismatches = mismatches, TotalChecked = totalChecked };
    }
}
