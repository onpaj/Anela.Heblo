using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using AutoMapper;
using Rem.FlexiBeeSDK.Client.Clients.Products.StockToDate;

namespace Anela.Heblo.Adapters.Flexi.Stock;

public class FlexiStockClient : IErpStockClient
{
    public const int MaterialWarehouseId = 5;
    public const int SemiProductsWarehouseId = 20;
    public const int ProductsWarehouseId = 4;

    private readonly IStockToDateClient _stockClient;
    private readonly TimeProvider _timeProvider;
    private readonly IMapper _mapper;

    public FlexiStockClient(IStockToDateClient stockClient, TimeProvider timeProvider, IMapper mapper)
    {
        _stockClient = stockClient;
        _timeProvider = timeProvider;
        _mapper = mapper;
    }

    public async Task<IReadOnlyList<ErpStock>> ListAsync(CancellationToken cancellationToken)
    {
        var materialStockTask = ListByWarehouse(MaterialWarehouseId, ProductType.Material, cancellationToken);
        var semiProductsStockTask = ListByWarehouse(SemiProductsWarehouseId, ProductType.SemiProduct, cancellationToken);
        var productsStockTask = ListByWarehouse(ProductsWarehouseId, new[] { ProductType.Product, ProductType.Goods }, cancellationToken);

        await Task.WhenAll(materialStockTask, semiProductsStockTask, productsStockTask);

        return materialStockTask.Result
            .Union(semiProductsStockTask.Result)
            .Union(productsStockTask.Result)
            .Where(w => !w.ProductName.Contains("archiv", StringComparison.InvariantCultureIgnoreCase)) // So far just convention in productname
            .ToList();
    }

    public async Task<IReadOnlyList<ErpStock>> StockToDateAsync(DateTime date, int warehouseId,
        CancellationToken cancellationToken)
    {
        // Map store code to warehouse ID
        var stockToDate = await _stockClient
            .GetAsync(date, warehouseId: warehouseId, cancellationToken: cancellationToken);

        var stock = _mapper.Map<List<ErpStock>>(stockToDate);
        return stock;
    }

    private Task<IReadOnlyList<ErpStock>> ListByWarehouse(int warehouseId, ProductType productType,
        CancellationToken cancellationToken)
    {
        return ListByWarehouse(warehouseId, new[] { productType }, cancellationToken);
    }

    private async Task<IReadOnlyList<ErpStock>> ListByWarehouse(int warehouseId, ProductType[] productTypes,
        CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        var parameters = new Dictionary<string, object>
        {
            ["warehouseId"] = warehouseId,
            ["productTypes"] = string.Join(", ", productTypes),
            ["date"] = _timeProvider.GetUtcNow().Date
        };

        var stockToDate = await _stockClient
            .GetAsync(_timeProvider.GetUtcNow().Date, warehouseId: warehouseId, cancellationToken: cancellationToken);

        var productTypeIds = productTypes.Select(i => (int?)i).ToList();
        var filteredStockToDate = stockToDate.Where(w => productTypeIds.Contains(w.ProductTypeId));
        var stock = _mapper.Map<List<ErpStock>>(filteredStockToDate);
        return stock;
    }
}