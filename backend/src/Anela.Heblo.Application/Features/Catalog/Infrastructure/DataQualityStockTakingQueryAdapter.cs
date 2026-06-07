using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Domain.Features.Catalog.Stock;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

internal sealed class DataQualityStockTakingQueryAdapter : IStockTakingQuery
{
    private readonly IStockTakingRepository _repository;

    public DataQualityStockTakingQueryAdapter(IStockTakingRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<StockTakingSnapshot>> GetByDateRangeAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        var records = await _repository.GetByDateRangeAsync(fromUtc, toUtc, cancellationToken);

        var snapshots = new List<StockTakingSnapshot>(records.Count);
        foreach (var record in records)
        {
            snapshots.Add(new StockTakingSnapshot
            {
                Code = record.Code,
                AmountNew = record.AmountNew,
                Error = record.Error,
            });
        }

        return snapshots;
    }
}
