namespace Anela.Heblo.Application.Features.DataQuality.Contracts;

public interface IStockOperationQuery
{
    Task<IReadOnlyList<StockOperationSnapshot>> GetByCreatedDateRangeAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default);
}
