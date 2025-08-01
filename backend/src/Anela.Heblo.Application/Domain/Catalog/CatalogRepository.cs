using System.Linq.Expressions;
using Anela.Heblo.Application.Domain.Catalog.Attributes;
using Anela.Heblo.Application.Domain.Catalog.ConsumedMaterials;
using Anela.Heblo.Application.Domain.Catalog.Lots;
using Anela.Heblo.Application.Domain.Catalog.Price;
using Anela.Heblo.Application.Domain.Catalog.PurchaseHistory;
using Anela.Heblo.Application.Domain.Catalog.Sales;
using Anela.Heblo.Application.Domain.Catalog.Stock;
using Anela.Heblo.Application.Domain.Logistics.Transport;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Domain.Catalog;

public class CatalogRepository : ICatalogRepository
{
    private readonly ICatalogSalesClient _salesClient;
    private readonly ICatalogAttributesClient _attributesClient;
    private readonly IEshopStockClient _eshopStockClient;
    private readonly IConsumedMaterialsClient _consumedMaterialClient;
    private readonly IPurchaseHistoryClient _purchaseHistoryClient;
    private readonly IErpStockClient _erpStockClient;
    private readonly ILotsClient _lotsClient;
    private readonly IProductPriceEshopClient _productPriceEshopClient;
    private readonly IProductPriceErpClient _productPriceErpClient;
    private readonly ITransportBoxRepository _transportBoxRepository;
    private readonly IStockTakingRepository _stockTakingRepository;
    private readonly IMemoryCache _cache;
    private readonly TimeProvider _timeProvider;
    private readonly IOptions<CatalogRepositoryOptions> _options;
    private readonly ILogger<CatalogRepository> _logger;


    public CatalogRepository(
        ICatalogSalesClient salesClient,
        ICatalogAttributesClient attributesClient,
        IEshopStockClient eshopStockClient,
        IConsumedMaterialsClient consumedMaterialClient,
        IPurchaseHistoryClient purchaseHistoryClient,
        IErpStockClient erpStockClient,
        ILotsClient lotsClient,
        IProductPriceEshopClient productPriceEshopClient,
        IProductPriceErpClient productPriceErpClient,
        ITransportBoxRepository transportBoxRepository,
        IStockTakingRepository stockTakingRepository,
        IMemoryCache cache,
        TimeProvider timeProvider,
        IOptions<CatalogRepositoryOptions> _options,
        ILogger<CatalogRepository> logger)
    {
        _salesClient = salesClient;
        _attributesClient = attributesClient;
        _eshopStockClient = eshopStockClient;
        _consumedMaterialClient = consumedMaterialClient;
        _purchaseHistoryClient = purchaseHistoryClient;
        _erpStockClient = erpStockClient;
        _lotsClient = lotsClient;
        _productPriceEshopClient = productPriceEshopClient;
        _productPriceErpClient = productPriceErpClient;
        _transportBoxRepository = transportBoxRepository;
        _stockTakingRepository = stockTakingRepository;
        _cache = cache;
        _timeProvider = timeProvider;
        this._options = _options;
        _logger = logger;
    }


    // public async Task UpdateStockAsync(UpdateCatalogStockRequest updateRequest, CancellationToken cancellationToken = default)
    // {
    //     var product = CatalogData.FirstOrDefault(s => s.ProductCode == updateRequest.ProductCode);
    //
    //     if (product == null)
    //         return;
    //
    //     if (product.Stock.PrimaryStockSource == StockSource.Eshop && product.Stock.Eshop != updateRequest.OnStockEshop)
    //     {
    //         await RefreshEshopStockData(cancellationToken);
    //     }
    //     else if(product.Stock.PrimaryStockSource == StockSource.Erp && product.Stock.Erp != updateRequest.OnStockErp)
    //     {
    //         await RefreshErpStockData(cancellationToken);
    //     }
    //
    //     await RefreshLostData(cancellationToken);
    //     await RefreshStockTakingData(cancellationToken);
    // }

    public async Task RefreshTransportData(CancellationToken ct)
    {
        var transportData = await GetProductsInTransport(ct);
        CachedInTransportData = transportData;
    }

    public async Task RefreshReserveData(CancellationToken ct)
    {
        var reserveData = await GetProductsInReserve(ct);
        CachedInReserveData = reserveData;
    }

    public async Task RefreshSalesData(CancellationToken ct)
    {
        CachedSalesData = await _salesClient.GetAsync(_timeProvider.GetUtcNow().Date.AddDays(-1 * _options.Value.SalesHistoryDays),
            _timeProvider.GetUtcNow().Date, cancellationToken: ct);
    }

    public async Task RefreshAttributesData(CancellationToken ct)
    {
        CachedCatalogAttributesData = await _attributesClient.GetAttributesAsync(cancellationToken: ct);
    }

    public async Task RefreshErpStockData(CancellationToken ct)
    {
        CachedErpStockData = (await _erpStockClient.ListAsync(ct)).ToList();
    }

    public async Task RefreshEshopStockData(CancellationToken ct)
    {
        CachedEshopStockData = (await _eshopStockClient.ListAsync(ct)).ToList();
    }

    public async Task RefreshPurchaseHistoryData(CancellationToken ct)
    {
        CachedPurchaseHistoryData = (await _purchaseHistoryClient.GetHistoryAsync(null, _timeProvider.GetUtcNow().Date.AddDays(-1 * _options.Value.PurchaseHistoryDays), _timeProvider.GetUtcNow().Date, cancellationToken: ct))
            .ToList();
    }

    public async Task RefreshConsumedHistoryData(CancellationToken ct)
    {
        CachedConsumedData = (await _consumedMaterialClient.GetConsumedAsync(_timeProvider.GetUtcNow().Date.AddDays(-1 * _options.Value.ConsumedHistoryDays), _timeProvider.GetUtcNow().Date, cancellationToken: ct))
            .ToList();
    }

    public async Task RefreshStockTakingData(CancellationToken ct)
    {
        CachedStockTakingData = (await _stockTakingRepository.GetAllAsync(ct)).ToList();
    }

    public async Task RefreshLotsData(CancellationToken ct)
    {
        CachedLotsData = (await _lotsClient.GetAsync(cancellationToken: ct)).ToList();
    }

    public async Task RefreshEshopPricesData(CancellationToken ct)
    {
        CachedEshopPriceData = (await _productPriceEshopClient.GetAllAsync(ct)).ToList();
    }

    public async Task RefreshErpPricesData(CancellationToken ct)
    {
        CachedErpPriceData = (await _productPriceErpClient.GetAllAsync(false, ct)).ToList();
    }


    private List<CatalogAggregate> CatalogData => _cache.GetOrCreate(nameof(CatalogData), c => Merge())!;

    private List<CatalogAggregate> Merge()
    {
        var products = CachedErpStockData.Select(s => new CatalogAggregate()
        {
            ProductCode = s.ProductCode,
            ProductName = s.ProductName,
            ErpId = s.ProductId,
            Stock = new StockData()
            {
                Erp = s.Stock
            },
            Type = (ProductType?)s.ProductTypeId ?? ProductType.UNDEFINED,
            MinimalOrderQuantity = s.MOQ,
            HasLots = s.HasLots,
            HasExpiration = s.HasExpiration,
            Volume = s.Volume,
            Weight = s.Weight,
        }).ToList();

        var attributesMap = CachedCatalogAttributesData.ToDictionary(k => k.ProductCode, v => v);
        var eshopProductsMap = CachedEshopStockData.ToDictionary(k => k.Code, v => v);
        var consumedMap = CachedConsumedData
            .GroupBy(p => p.ProductCode)
            .ToDictionary(k => k.Key, v => v.ToList());
        var purchaseMap = CachedPurchaseHistoryData
            .GroupBy(p => p.ProductCode)
            .ToDictionary(k => k.Key, v => v.ToList());
        var stockTakingMap = CachedStockTakingData
            .GroupBy(p => p.Code)
            .ToDictionary(k => k.Key, v => v.ToList());
        var lotsMap = CachedLotsData
            .GroupBy(p => p.ProductCode)
            .ToDictionary(k => k.Key, v => v.ToList());
        var eshopPriceMap = CachedEshopPriceData.ToDictionary(k => k.ProductCode, v => v);
        var erpPriceMap = CachedErpPriceData.ToDictionary(k => k.ProductCode, v => v);

        foreach (var product in products)
        {
            product.SalesHistory = CachedSalesData.Where(w => w.ProductCode == product.ProductCode).ToList();

            if (attributesMap.TryGetValue(product.ProductCode, out var attributes))
            {
                product.Properties.OptimalStockDaysSetup = attributes.OptimalStockDays;
                product.Properties.StockMinSetup = attributes.StockMin;
                product.Properties.BatchSize = attributes.BatchSize;
                product.Properties.SeasonMonths = attributes.SeasonMonthsArray;
                product.MinimalManufactureQuantity = attributes.MinimalManufactureQuantity;
            }

            if (CachedInTransportData.TryGetValue(product.ProductCode, out var inTransport))
            {
                product.Stock.Transport = inTransport;
            }

            if (CachedInReserveData.TryGetValue(product.ProductCode, out var inReserve))
            {
                product.Stock.Reserve = inReserve;
            }

            if (eshopProductsMap.TryGetValue(product.ProductCode, out var eshopProduct))
            {
                product.Stock.Eshop = eshopProduct.Stock;
                product.Stock.PrimaryStockSource = StockSource.Eshop;
                product.Location = eshopProduct.Location;
            }

            if (consumedMap.TryGetValue(product.ProductCode, out var consumed))
            {
                product.ConsumedHistory = consumed.ToList();
            }

            if (purchaseMap.TryGetValue(product.ProductCode, out var purchases))
            {
                product.PurchaseHistory = purchases.ToList();
            }

            if (stockTakingMap.TryGetValue(product.ProductCode, out var stockTakings))
            {
                product.StockTakingHistory = stockTakings.OrderByDescending(o => o.Date).ToList();
            }

            if (lotsMap.TryGetValue(product.ProductCode, out var lots))
            {
                product.Stock.Lots = lots.ToList();
            }

            // Mapování eshop cen podle ProductCode
            if (eshopPriceMap.TryGetValue(product.ProductCode, out var eshopPrice))
            {
                product.EshopPrice = eshopPrice;
            }

            // Mapování ERP cen podle ProductCode
            if (erpPriceMap.TryGetValue(product.ProductCode, out var erpPrice))
            {
                product.ErpPrice = erpPrice;
            }
        }

        return products.ToList();
    }

    private void Invalidate()
    {
        _cache.Remove(nameof(CatalogData));
    }

    private IList<CatalogSaleRecord> CachedSalesData
    {
        get => _cache.Get<List<CatalogSaleRecord>>(nameof(CachedSalesData)) ?? new List<CatalogSaleRecord>();
        set
        {
            _cache.Set(nameof(CachedSalesData), value);
            Invalidate();
        }
    }



    private IList<CatalogAttributes> CachedCatalogAttributesData
    {
        get => _cache.Get<List<CatalogAttributes>>(nameof(CachedCatalogAttributesData)) ?? new List<CatalogAttributes>();
        set
        {
            _cache.Set(nameof(CachedCatalogAttributesData), value);
            Invalidate();
        }
    }
    private IDictionary<string, int> CachedInTransportData
    {
        get => _cache.Get<Dictionary<string, int>>(nameof(CachedInTransportData)) ?? new Dictionary<string, int>();
        set
        {
            _cache.Set(nameof(CachedInTransportData), value);
            Invalidate();
        }
    }

    private IDictionary<string, int> CachedInReserveData
    {
        get => _cache.Get<Dictionary<string, int>>(nameof(CachedInReserveData)) ?? new Dictionary<string, int>();
        set
        {
            _cache.Set(nameof(CachedInReserveData), value);
            Invalidate();
        }
    }

    private IList<ErpStock> CachedErpStockData
    {
        get => _cache.Get<List<ErpStock>>(nameof(CachedErpStockData)) ?? new List<ErpStock>();
        set
        {
            _cache.Set(nameof(CachedErpStockData), value);
            Invalidate();
        }
    }
    private IList<EshopStock> CachedEshopStockData
    {
        get => _cache.Get<List<EshopStock>>(nameof(CachedEshopStockData)) ?? new List<EshopStock>();
        set
        {
            _cache.Set(nameof(CachedEshopStockData), value);
            Invalidate();
        }
    }
    private IList<CatalogPurchaseRecord> CachedPurchaseHistoryData
    {
        get => _cache.Get<List<CatalogPurchaseRecord>>(nameof(CachedPurchaseHistoryData)) ?? new List<CatalogPurchaseRecord>();
        set
        {
            _cache.Set(nameof(CachedPurchaseHistoryData), value);
            Invalidate();
        }
    }
    private IList<ConsumedMaterialRecord> CachedConsumedData
    {
        get => _cache.Get<List<ConsumedMaterialRecord>>(nameof(CachedConsumedData)) ?? new List<ConsumedMaterialRecord>();
        set
        {
            _cache.Set(nameof(CachedConsumedData), value);
            Invalidate();
        }
    }

    private IList<StockTakingRecord> CachedStockTakingData
    {
        get => _cache.Get<List<StockTakingRecord>>(nameof(CachedStockTakingData)) ?? new List<StockTakingRecord>();
        set
        {
            _cache.Set(nameof(CachedStockTakingData), value);
            Invalidate();
        }
    }

    private IList<CatalogLot> CachedLotsData
    {
        get => _cache.Get<List<CatalogLot>>(nameof(CachedLotsData)) ?? new List<CatalogLot>();
        set
        {
            _cache.Set(nameof(CachedLotsData), value);
            Invalidate();
        }
    }

    private IList<ProductPriceEshop> CachedEshopPriceData
    {
        get => _cache.Get<List<ProductPriceEshop>>(nameof(CachedEshopPriceData)) ?? new List<ProductPriceEshop>();
        set
        {
            _cache.Set(nameof(CachedEshopPriceData), value);
            Invalidate();
        }
    }

    private IList<ProductPriceErp> CachedErpPriceData
    {
        get => _cache.Get<List<ProductPriceErp>>(nameof(CachedErpPriceData)) ?? new List<ProductPriceErp>();
        set
        {
            _cache.Set(nameof(CachedErpPriceData), value);
            Invalidate();
        }
    }


    private async Task<Dictionary<string, int>> GetProductsInTransport(CancellationToken ct)
    {
        var boxes = await _transportBoxRepository.FindAsync(TransportBox.IsInTransportPredicate, cancellationToken: ct);
        return boxes.SelectMany(s => s.Items)
            .GroupBy(g => g.ProductCode)
            .ToDictionary(k => k.Key, v => v.Sum(s => (int)s.Amount));
    }

    private async Task<Dictionary<string, int>> GetProductsInReserve(CancellationToken ct)
    {
        var boxes = await _transportBoxRepository.FindAsync(TransportBox.IsInReservePredicate, cancellationToken: ct);
        return boxes.SelectMany(s => s.Items)
            .GroupBy(g => g.ProductCode)
            .ToDictionary(k => k.Key, v => v.Sum(s => (int)s.Amount));
    }

    public Task<CatalogAggregate?> GetByIdAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult(CatalogData.SingleOrDefault(s => s.ProductCode == id));
    public Task<IEnumerable<CatalogAggregate>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult(CatalogData.AsEnumerable());
    public Task<IEnumerable<CatalogAggregate>> FindAsync(Expression<Func<CatalogAggregate, bool>> predicate, CancellationToken cancellationToken = default) => Task.FromResult(CatalogData.AsQueryable().Where(predicate).AsEnumerable());
    public Task<CatalogAggregate?> SingleOrDefaultAsync(Expression<Func<CatalogAggregate, bool>> predicate, CancellationToken cancellationToken = default) => Task.FromResult(CatalogData.AsQueryable().SingleOrDefault(predicate));
    public Task<bool> AnyAsync(Expression<Func<CatalogAggregate, bool>> predicate, CancellationToken cancellationToken = default) => Task.FromResult(CatalogData.AsQueryable().Any(predicate));
    public Task<int> CountAsync(Expression<Func<CatalogAggregate, bool>>? predicate = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(predicate == null ? CatalogData.Count : CatalogData.AsQueryable().Count(predicate));
}