using Anela.Heblo.Application.Domain.Catalog;
using Anela.Heblo.Application.Domain.Catalog.Stock;
using Anela.Heblo.Xcc.Audit;
using Rem.FlexiBeeSDK.Client.Clients.Products.StockToDate;

namespace Anela.Heblo.Adapters.Flexi.Stock;

public class FlexiStockClient : IErpStockClient
{
    private const int MaterialWarehouseId = 5;
    private const int SemiProductsWarehouseId = 20;
    private const int ProductsWarehouseId = 4;

    private readonly IStockToDateClient _stockClient;
    private readonly TimeProvider _timeProvider;
    private readonly IDataLoadAuditService _auditService;

    public FlexiStockClient(IStockToDateClient stockClient, TimeProvider timeProvider, IDataLoadAuditService auditService)
    {
        _stockClient = stockClient;
        _timeProvider = timeProvider;
        _auditService = auditService;
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
            .ToList();
    }


    private Task<IReadOnlyList<ErpStock>> ListByWarehouse(int warehouseId, ProductType productType,
        CancellationToken cancellationToken)
    {
        return ListByWarehouse(warehouseId, new[] { productType }, cancellationToken);
    }

    private async Task<IReadOnlyList<ErpStock>> ListByWarehouse(int warehouseId, ProductType[] productTypes, CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        var parameters = new Dictionary<string, object>
        {
            ["warehouseId"] = warehouseId,
            ["productTypes"] = string.Join(", ", productTypes),
            ["date"] = _timeProvider.GetUtcNow().Date
        };

        try
        {
            var stockToDate = await _stockClient
                .GetAsync(_timeProvider.GetUtcNow().Date, warehouseId: warehouseId, cancellationToken: cancellationToken);

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

            var duration = DateTime.UtcNow - startTime;
            await _auditService.LogDataLoadAsync(
                dataType: "Stock",
                source: "Flexi ERP",
                recordCount: stock.Count,
                success: true,
                parameters: parameters,
                duration: duration);

            return stock;
        }
        catch (Exception ex)
        {
            var duration = DateTime.UtcNow - startTime;
            await _auditService.LogDataLoadAsync(
                dataType: "Stock",
                source: "Flexi ERP",
                recordCount: 0,
                success: false,
                parameters: parameters,
                errorMessage: ex.Message,
                duration: duration);
            throw;
        }
    }
}