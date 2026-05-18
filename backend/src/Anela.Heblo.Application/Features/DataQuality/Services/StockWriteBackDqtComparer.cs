using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.DataQuality;

namespace Anela.Heblo.Application.Features.DataQuality.Services;

public class StockWriteBackDqtComparer : IDriftDqtComparer
{
    private static readonly TimeSpan DefaultStuckThreshold = TimeSpan.FromHours(1);

    private readonly IStockUpOperationRepository _operationRepository;
    private readonly IStockTakingRepository _stockTakingRepository;
    private readonly TimeSpan _stuckThreshold;

    public DqtTestType TestType => DqtTestType.StockWriteBackReconciliation;

    public StockWriteBackDqtComparer(
        IStockUpOperationRepository operationRepository,
        IStockTakingRepository stockTakingRepository,
        TimeSpan? stuckThreshold = null)
    {
        _operationRepository = operationRepository;
        _stockTakingRepository = stockTakingRepository;
        _stuckThreshold = stuckThreshold ?? DefaultStuckThreshold;
    }

    public async Task<DriftComparisonResult> CompareAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var fromUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc = to.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
        var stuckCutoff = DateTime.UtcNow - _stuckThreshold;

        var operations = _operationRepository.GetAll()
            .Where(o => o.CreatedAt >= fromUtc && o.CreatedAt <= toUtc)
            .ToList();

        var stockTakingRecords = await _stockTakingRepository.GetByDateRangeAsync(fromUtc, toUtc, ct);

        var mismatches = new List<DriftMismatch>();

        foreach (var op in operations)
        {
            var mismatch = StockWriteBackMismatch.None;

            if (op.State == StockUpOperationState.Failed)
                mismatch |= StockWriteBackMismatch.OperationFailed;

            if ((op.State == StockUpOperationState.Pending || op.State == StockUpOperationState.Submitted)
                && op.CreatedAt <= stuckCutoff)
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

    private static string BuildOperationDetails(StockUpOperation op)
    {
        var parts = new List<string> { $"Doc: {op.DocumentNumber}", $"State: {op.State}" };
        if (!string.IsNullOrWhiteSpace(op.ErrorMessage))
            parts.Add($"Error: {op.ErrorMessage}");
        return string.Join(" | ", parts);
    }
}
