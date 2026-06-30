using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.DataQuality;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.DataQuality.Services;

public class ProductPairingDqtComparer : IDriftDqtComparer
{
    private readonly IEshopStockClient _eshopStockClient;
    private readonly IErpStockClient _erpStockClient;
    private readonly ICatalogResilienceService _resilienceService;
    private readonly ILogger<ProductPairingDqtComparer> _logger;

    public DqtTestType TestType => DqtTestType.ProductPairing;

    public ProductPairingDqtComparer(
        IEshopStockClient eshopStockClient,
        IErpStockClient erpStockClient,
        ICatalogResilienceService resilienceService,
        ILogger<ProductPairingDqtComparer> logger)
    {
        _eshopStockClient = eshopStockClient;
        _erpStockClient = erpStockClient;
        _resilienceService = resilienceService;
        _logger = logger;
    }

    public async Task<DriftComparisonResult> CompareAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        // Date range is intentionally unused — product pairing is a current-state snapshot
        List<EshopStock> eshopProducts;
        try
        {
            eshopProducts = await _resilienceService.ExecuteWithResilienceAsync(
                async cancellationToken => await _eshopStockClient.ListAsync(cancellationToken),
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

        IReadOnlyList<ErpStock> erpProducts;
        try
        {
            erpProducts = await _resilienceService.ExecuteWithResilienceAsync(
                async cancellationToken => await _erpStockClient.ListAsync(cancellationToken),
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

        var sellableErpProducts = erpProducts.Where(IsSellable).ToList();

        var erpCodeSet = sellableErpProducts
            .Select(p => p.ProductCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // All Shoptet identifiers (Code + PairCode) used when checking ERP → Shoptet direction
        var shoptetIdentifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in eshopProducts)
        {
            shoptetIdentifiers.Add(p.Code);
            if (!string.IsNullOrWhiteSpace(p.PairCode))
                shoptetIdentifiers.Add(p.PairCode);
        }

        var mismatches = new List<DriftMismatch>();

        // Check A: each Shoptet product must resolve to an ERP code
        foreach (var eshopProduct in eshopProducts)
        {
            var hasPairCode = !string.IsNullOrWhiteSpace(eshopProduct.PairCode);
            var resolvedCode = hasPairCode ? eshopProduct.PairCode : eshopProduct.Code;

            if (erpCodeSet.Contains(resolvedCode))
                continue;

            var mismatch = ProductPairingMismatch.MissingInErp;
            if (hasPairCode)
                mismatch |= ProductPairingMismatch.PairCodeUnresolved;

            mismatches.Add(new DriftMismatch
            {
                EntityKey = eshopProduct.Code,
                MismatchCode = (int)mismatch,
                ShoptetValue = eshopProduct.Name,
                HebloValue = null,
                Details = hasPairCode
                    ? $"Shoptet product '{eshopProduct.Code}' PairCode '{eshopProduct.PairCode}' not found in ERP"
                    : $"Shoptet product '{eshopProduct.Code}' not found in ERP"
            });
        }

        // Check B: each sellable ERP product must appear in Shoptet
        foreach (var erpProduct in sellableErpProducts)
        {
            if (shoptetIdentifiers.Contains(erpProduct.ProductCode))
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

        var totalChecked = shoptetIdentifiers
            .Union(erpCodeSet, StringComparer.OrdinalIgnoreCase)
            .Count();

        return new DriftComparisonResult { Mismatches = mismatches, TotalChecked = totalChecked };
    }

    private static bool IsSellable(ErpStock product) =>
        product.ProductTypeId == (int)ProductType.Goods ||
        product.ProductTypeId == (int)ProductType.Product;
}
