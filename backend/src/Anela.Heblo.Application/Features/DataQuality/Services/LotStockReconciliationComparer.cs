using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Domain.Features.DataQuality;

namespace Anela.Heblo.Application.Features.DataQuality.Services;

/// <summary>
/// Reconciles, per material with expiration, the sum of loaded stock lots against the
/// item's ERP on-hand stock. Surfaces drift (stale, missing, or orphan lots) that would
/// otherwise be invisible between explicit stock-takings.
/// </summary>
public class LotStockReconciliationComparer : IDriftDqtComparer
{
    private const decimal Tolerance = 0.01m;

    private readonly IMaterialLotStockQuery _materialLotStock;

    public DqtTestType TestType => DqtTestType.LotSumVsErpStock;

    public LotStockReconciliationComparer(IMaterialLotStockQuery materialLotStock)
    {
        _materialLotStock = materialLotStock;
    }

    public async Task<DriftComparisonResult> CompareAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        // Date range is intentionally unused — this is a current-state snapshot reconciliation.
        var items = await _materialLotStock.GetMaterialsWithExpirationAsync(ct);

        var mismatches = new List<DriftMismatch>();

        foreach (var item in items)
        {
            var erp = item.ErpStock;
            var lotSum = item.LotAmounts.Sum();

            if (Math.Abs(lotSum - erp) <= Tolerance)
                continue;

            var flag = Classify(erp, lotSum);

            mismatches.Add(new DriftMismatch
            {
                EntityKey = item.ProductCode,
                MismatchCode = (int)flag,
                HebloValue = erp.ToString("F2"),
                ShoptetValue = lotSum.ToString("F2"),
                Details = $"ERP: {erp:F2} | Šarže: {lotSum:F2} | Rozdíl: {lotSum - erp:F2}"
            });
        }

        return new DriftComparisonResult
        {
            Mismatches = mismatches,
            TotalChecked = items.Count
        };
    }

    private static LotStockReconciliationMismatch Classify(decimal erp, decimal lotSum)
    {
        if (lotSum <= Tolerance)
            return LotStockReconciliationMismatch.MissingLots;

        if (erp <= Tolerance)
            return LotStockReconciliationMismatch.OrphanLots;

        return LotStockReconciliationMismatch.SumMismatch;
    }
}
