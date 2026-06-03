namespace Anela.Heblo.Application.Features.DataQuality.Contracts;

public interface IStockTakingQuery
{
    Task<IReadOnlyList<StockTakingSnapshot>> GetByDateRangeAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default);
}
