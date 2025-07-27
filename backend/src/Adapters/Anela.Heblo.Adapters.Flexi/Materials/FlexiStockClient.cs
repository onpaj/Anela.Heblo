using Anela.Heblo.Catalog;
using Anela.Heblo.Catalog.Stock;
using Anela.Heblo.Data;
using Anela.Heblo.Products;
using Rem.FlexiBeeSDK.Client.Clients.Products.StockToDate;
using Volo.Abp.Timing;

namespace Anela.Heblo.Adapters.Flexi.Materials;

public  class FlexiStockClient : IErpStockClient
{
    private const int MaterialWarehouseId = 5;
    private const int SemiProductsWarehouseId = 20;
    private const int ProductsWarehouseId = 4;
    
    private readonly IStockToDateClient _stockClient;
    private readonly IClock _clock;
    private readonly ISynchronizationContext _synchronizationContext;

    public FlexiStockClient(IStockToDateClient stockClient, IClock clock, ISynchronizationContext synchronizationContext)
    {
        _stockClient = stockClient;
        _clock = clock;
        _synchronizationContext = synchronizationContext;
    }
    
    public async Task<IReadOnlyList<ErpStock>> ListAsync(CancellationToken cancellationToken)
    {
        var materialStockTask = ListByWarehouse(MaterialWarehouseId, ProductType.Material, cancellationToken);
        var semiProductsStockTask = ListByWarehouse(SemiProductsWarehouseId, ProductType.SemiProduct, cancellationToken);
        var productsStockTask = ListByWarehouse(ProductsWarehouseId, new [] { ProductType.Product, ProductType.Goods}, cancellationToken);
        
        await Task.WhenAll( materialStockTask, semiProductsStockTask, productsStockTask);

        return materialStockTask.Result
            .Union(semiProductsStockTask.Result)
            .Union(productsStockTask.Result)
            .ToList();
    }


    private Task<IReadOnlyList<ErpStock>> ListByWarehouse(int warehouseId, ProductType productType,
        CancellationToken cancellationToken)
    {
        return ListByWarehouse(warehouseId, new [] {productType}, cancellationToken);
    }

    private async Task<IReadOnlyList<ErpStock>> ListByWarehouse(int warehouseId, ProductType[] productTypes, CancellationToken cancellationToken)
    {
        var stockToDate = await _stockClient
            .GetAsync(_clock.Now.Date, warehouseId: warehouseId, cancellationToken: cancellationToken);

        var productTypeIds = productTypes.Select(i => (int?)i).ToList();
        var stock = stockToDate
            .Where(w => productTypeIds.Contains(w.ProductTypeId))
            .Select(s => new ErpStock
            {
                ProductCode = s.ProductCode,
                ProductName = s.ProductName,
                ProductTypeId = s.ProductTypeId,
                ProductId = s.ProductId,
                Stock = (decimal)s.OnStock,
                MOQ = s.MoqName,
                HasLots = s.HasLots,
                HasExpiration = s.HasExpiration,
                Volume = s.Volume,
                Weight = s.Weight
            }).ToList();

        _synchronizationContext.Submit(new ErpStockSyncData(stock, warehouseId));
        
        return stock;
    }
}