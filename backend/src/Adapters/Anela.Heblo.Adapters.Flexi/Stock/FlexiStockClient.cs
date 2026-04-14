using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using AutoMapper;
using Microsoft.Extensions.Logging;
using Rem.FlexiBeeSDK.Client.Clients.Products.StockToDate;
using System.Net;

namespace Anela.Heblo.Adapters.Flexi.Stock;

public class FlexiStockClient : IErpStockClient
{
    public const int MaterialWarehouseId = 5;
    public const int SemiProductsWarehouseId = 20;
    public const int ProductsWarehouseId = 4;

    private readonly IStockToDateClient _stockClient;
    private readonly TimeProvider _timeProvider;
    private readonly IMapper _mapper;
    private readonly ILogger<FlexiStockClient> _logger;

    public FlexiStockClient(
        IStockToDateClient stockClient,
        TimeProvider timeProvider,
        IMapper mapper,
        ILogger<FlexiStockClient> logger)
    {
        _stockClient = stockClient;
        _timeProvider = timeProvider;
        _mapper = mapper;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
        try
        {
            var stockToDate = await _stockClient
                .GetAsync(date, warehouseId: warehouseId, cancellationToken: cancellationToken);

            var stock = _mapper.Map<List<ErpStock>>(stockToDate);
            return stock;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotImplemented)
        {
            _logger.LogError(ex,
                "FlexiBee stav-skladu-k-datu returned 501 NotImplemented — endpoint may be disabled or unsupported on this instance. " +
                "WarehouseId: {WarehouseId}, Date: {Date}",
                warehouseId, date);
            throw;
        }
    }

    private Task<IReadOnlyList<ErpStock>> ListByWarehouse(int warehouseId, ProductType productType,
        CancellationToken cancellationToken)
    {
        return ListByWarehouse(warehouseId, new[] { productType }, cancellationToken);
    }

    private async Task<IReadOnlyList<ErpStock>> ListByWarehouse(int warehouseId, ProductType[] productTypes,
        CancellationToken cancellationToken)
    {
        try
        {
            var stockToDate = await _stockClient
                .GetAsync(_timeProvider.GetUtcNow().Date, warehouseId: warehouseId, cancellationToken: cancellationToken);

            var productTypeIds = productTypes.Select(i => (int?)i).ToList();
            var filteredStockToDate = stockToDate.Where(w => productTypeIds.Contains(w.ProductTypeId));
            var stock = _mapper.Map<List<ErpStock>>(filteredStockToDate);
            return stock;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotImplemented)
        {
            _logger.LogError(ex,
                "FlexiBee stav-skladu-k-datu returned 501 NotImplemented — endpoint may be disabled or unsupported on this instance. " +
                "WarehouseId: {WarehouseId}, ProductTypes: {ProductTypes}",
                warehouseId, string.Join(", ", productTypes));
            throw;
        }
    }
}
