using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Domain.Features.Catalog.Stock;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

internal sealed class DataQualityEshopStockSourceAdapter : IDqtEshopStockSource
{
    private readonly IEshopStockClient _inner;

    public DataQualityEshopStockSourceAdapter(IEshopStockClient inner)
    {
        _inner = inner;
    }

    public async Task<IReadOnlyList<DqtEshopStockItem>> ListAsync(CancellationToken cancellationToken)
    {
        var products = await _inner.ListAsync(cancellationToken);
        return products
            .Select(p => new DqtEshopStockItem { Code = p.Code, PairCode = p.PairCode, Name = p.Name })
            .ToList();
    }
}
