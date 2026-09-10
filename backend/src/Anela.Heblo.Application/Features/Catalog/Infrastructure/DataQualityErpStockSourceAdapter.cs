using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

internal sealed class DataQualityErpStockSourceAdapter : IDqtErpStockSource
{
    private readonly IErpStockClient _inner;

    public DataQualityErpStockSourceAdapter(IErpStockClient inner)
    {
        _inner = inner;
    }

    public async Task<IReadOnlyList<DqtErpStockItem>> ListAsync(CancellationToken cancellationToken)
    {
        var products = await _inner.ListAsync(cancellationToken);
        return products
            .Select(p => new DqtErpStockItem
            {
                ProductCode = p.ProductCode,
                ProductName = p.ProductName,
                IsSellable = p.ProductTypeId == (int)ProductType.Goods || p.ProductTypeId == (int)ProductType.Product,
            })
            .ToList();
    }
}
