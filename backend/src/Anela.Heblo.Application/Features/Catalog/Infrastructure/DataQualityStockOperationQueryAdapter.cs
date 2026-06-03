using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Domain.Features.Catalog.Stock;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

internal sealed class DataQualityStockOperationQueryAdapter : IStockOperationQuery
{
    private readonly IStockUpOperationRepository _repository;

    public DataQualityStockOperationQueryAdapter(IStockUpOperationRepository repository)
    {
        _repository = repository;
    }

    public Task<IReadOnlyList<StockOperationSnapshot>> GetByCreatedDateRangeAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        var snapshots = _repository.GetAll()
            .Where(o => o.CreatedAt >= fromUtc && o.CreatedAt <= toUtc)
            .Select(o => new StockOperationSnapshot
            {
                ProductCode = o.ProductCode,
                Amount = o.Amount,
                DocumentNumber = o.DocumentNumber,
                State = MapState(o.State),
                CreatedAtUtc = o.CreatedAt,
                ErrorMessage = o.ErrorMessage,
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<StockOperationSnapshot>>(snapshots);
    }

    private static StockOperationStateSnapshot MapState(StockUpOperationState state) => state switch
    {
        StockUpOperationState.Pending => StockOperationStateSnapshot.Pending,
        StockUpOperationState.Submitted => StockOperationStateSnapshot.Submitted,
        StockUpOperationState.Completed => StockOperationStateSnapshot.Completed,
        StockUpOperationState.Failed => StockOperationStateSnapshot.Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };
}
