using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Xcc.Audit;
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
    private readonly IDataLoadAuditService _auditService;
    private readonly IMapper _mapper;

    public FlexiStockClient(IStockToDateClient stockClient, TimeProvider timeProvider, IDataLoadAuditService auditService, IMapper mapper)
    {
        _stockClient = stockClient;
        _timeProvider = timeProvider;
        _auditService = auditService;
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

    public async Task<IReadOnlyList<ErpStock>> StockToDateAsync(DateTime date, int warehouseId, CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        var parameters = new Dictionary<string, object>
        {
            ["date"] = date,
            ["warehouseId"] = warehouseId
        };

        try
        {
            // Map store code to warehouse ID
            var stockToDate = await _stockClient
                .GetAsync(date, warehouseId: warehouseId, cancellationToken: cancellationToken);

            var stock = _mapper.Map<List<ErpStock>>(stockToDate);

            var duration = DateTime.UtcNow - startTime;
            await _auditService.LogDataLoadAsync(
                dataType: "StockToDate",
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
                dataType: "StockToDate",
                source: "Flexi ERP",
                recordCount: 0,
                success: false,
                parameters: parameters,
                errorMessage: ex.Message,
                duration: duration);
            throw;
        }
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
            var filteredStockToDate = stockToDate.Where(w => productTypeIds.Contains(w.ProductTypeId));
            var stock = _mapper.Map<List<ErpStock>>(filteredStockToDate);

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