using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Domain.Features.DataQuality;

namespace Anela.Heblo.Application.Features.DataQuality.Services;

public class StockWriteBackDqtComparer : IDriftDqtComparer
{
    private static readonly TimeSpan DefaultStuckThreshold = TimeSpan.FromHours(1);

    private readonly IStockOperationQuery _stockOperations;
    private readonly IStockTakingQuery _stockTakings;
    private readonly TimeSpan _stuckThreshold;

    public DqtTestType TestType => DqtTestType.StockWriteBackReconciliation;

    public StockWriteBackDqtComparer(
        IStockOperationQuery stockOperations,
        IStockTakingQuery stockTakings,
        TimeSpan? stuckThreshold = null)
    {
        _stockOperations = stockOperations;
        _stockTakings = stockTakings;
        _stuckThreshold = stuckThreshold ?? DefaultStuckThreshold;
    }

    public async Task<DriftComparisonResult> CompareAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var fromUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc = to.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
        var stuckCutoff = DateTime.UtcNow - _stuckThreshold;

        var operations = await _stockOperations.GetByCreatedDateRangeAsync(fromUtc, toUtc, ct);
        var stockTakingRecords = await _stockTakings.GetByDateRangeAsync(fromUtc, toUtc, ct);

        var mismatches = new List<DriftMismatch>();

        foreach (var op in operations)
        {
            var mismatch = StockWriteBackMismatch.None;

            if (op.State == StockOperationStateSnapshot.Failed)
                mismatch |= StockWriteBackMismatch.OperationFailed;

            if ((op.State == StockOperationStateSnapshot.Pending || op.State == StockOperationStateSnapshot.Submitted)
                && op.CreatedAtUtc <= stuckCutoff)
                mismatch |= StockWriteBackMismatch.OperationStuck;

            if (mismatch == StockWriteBackMismatch.None)
                continue;

            mismatches.Add(new DriftMismatch
            {
                EntityKey = op.ProductCode,
                MismatchCode = (int)mismatch,
                HebloValue = op.Amount.ToString(),
                ShoptetValue = null,
                Details = BuildOperationDetails(op)
            });
        }

        foreach (var record in stockTakingRecords.Where(r => r.Error != null))
        {
            mismatches.Add(new DriftMismatch
            {
                EntityKey = record.Code,
                MismatchCode = (int)StockWriteBackMismatch.StockTakingErrored,
                HebloValue = record.AmountNew.ToString("F2"),
                ShoptetValue = null,
                Details = $"Stock-taking error: {record.Error}"
            });
        }

        return new DriftComparisonResult
        {
            Mismatches = mismatches,
            TotalChecked = operations.Count + stockTakingRecords.Count
        };
    }

    private static string BuildOperationDetails(StockOperationSnapshot op)
    {
        var parts = new List<string> { $"Doc: {op.DocumentNumber}", $"State: {op.State}" };
        if (!string.IsNullOrWhiteSpace(op.ErrorMessage))
            parts.Add($"Error: {op.ErrorMessage}");
        return string.Join(" | ", parts);
    }
}
