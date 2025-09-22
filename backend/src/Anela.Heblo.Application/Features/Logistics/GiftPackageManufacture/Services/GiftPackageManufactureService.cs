using Anela.Heblo.Application.Features.Logistics.GiftPackageManufacture.Contracts;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Logistics.GiftPackageManufacture;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Users;
using Anela.Heblo.Xcc.Services;
using AutoMapper;

namespace Anela.Heblo.Application.Features.Logistics.GiftPackageManufacture.Services;

public class GiftPackageManufactureService : IGiftPackageManufactureService
{
    private readonly IManufactureRepository _manufactureRepository;
    private readonly IGiftPackageManufactureRepository _giftPackageRepository;
    private readonly ICatalogRepository _catalogRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IEshopStockDomainService _eshopStockDomainService;
    private readonly IMapper _mapper;
    private readonly TimeProvider _timeProvider;
    private readonly IBackgroundWorker _backgroundWorker;

    public GiftPackageManufactureService(
        IManufactureRepository manufactureRepository,
        IGiftPackageManufactureRepository giftPackageRepository,
        ICatalogRepository catalogRepository,
        ICurrentUserService currentUserService,
        IEshopStockDomainService eshopStockDomainService,
        IMapper mapper,
        TimeProvider timeProvider,
        IBackgroundWorker backgroundWorker)
    {
        _manufactureRepository = manufactureRepository;
        _giftPackageRepository = giftPackageRepository;
        _catalogRepository = catalogRepository;
        _currentUserService = currentUserService;
        _eshopStockDomainService = eshopStockDomainService;
        _mapper = mapper;
        _timeProvider = timeProvider;
        _backgroundWorker = backgroundWorker;
    }

    public async Task<List<GiftPackageDto>> GetAvailableGiftPackagesAsync(CancellationToken cancellationToken = default)
    {
        // Get all products with ProductType.Set from catalog
        var catalogData = await _catalogRepository.GetAllAsync(cancellationToken);
        var setProducts = catalogData.Where(x => x.Type == ProductType.Set).ToList();

        var giftPackages = new List<GiftPackageDto>();

        // Calculate date range for daily sales calculation (last 12 months)
        var toDate = _timeProvider.GetUtcNow().DateTime;
        var fromDate = toDate.AddYears(-1);
        var daysDiff = Math.Max((toDate - fromDate).Days, 1);

        foreach (var product in setProducts)
        {
            // Calculate daily sales from sales history
            var totalSalesInPeriod = product.GetTotalSold(fromDate, toDate);
            var dailySales = (decimal)(totalSalesInPeriod / daysDiff);

            var giftPackage = new GiftPackageDto
            {
                Code = product.ProductCode,
                Name = product.ProductName,
                AvailableStock = (int)product.Stock.Available,
                DailySales = dailySales,
                OverstockLimit = (int)product.Properties.StockMinSetup
                // Ingredients will be loaded separately when detail is requested
            };

            giftPackages.Add(giftPackage);
        }

        return giftPackages;
    }

    public async Task<GiftPackageDto> GetGiftPackageDetailAsync(string giftPackageCode, CancellationToken cancellationToken = default)
    {
        // Get the basic product info from catalog
        var product = await _catalogRepository.GetByIdAsync(giftPackageCode, cancellationToken);

        if (product == null || product.Type != ProductType.Set)
        {
            throw new ArgumentException($"Gift package '{giftPackageCode}' not found or is not a set product");
        }

        // Calculate date range for daily sales calculation (last 12 months)
        var toDate = _timeProvider.GetUtcNow().DateTime;
        var fromDate = toDate.AddYears(-1);
        var daysDiff = Math.Max((toDate - fromDate).Days, 1);

        // Calculate daily sales from sales history
        var totalSalesInPeriod = product.GetTotalSold(fromDate, toDate);
        var dailySales = (decimal)(totalSalesInPeriod / daysDiff);

        // Create the detailed gift package with ingredients
        var giftPackage = new GiftPackageDto
        {
            Code = product.ProductCode,
            Name = product.ProductName,
            AvailableStock = (int)product.Stock.Available,
            DailySales = dailySales,
            OverstockLimit = (int)product.Properties.StockMinSetup,
            Ingredients = new List<GiftPackageIngredientDto>()
        };

        // Load BOM (Bill of Materials) from manufacture repository
        var productParts = await _manufactureRepository.GetSetParts(giftPackageCode, cancellationToken);

        // Map ProductPart objects to GiftPackageIngredientDto with stock data
        var ingredients = new List<GiftPackageIngredientDto>();
        foreach (var part in productParts)
        {
            // Load product from catalog to get available stock
            var ingredientProduct = await _catalogRepository.GetByIdAsync(part.ProductCode, cancellationToken);

            var ingredient = new GiftPackageIngredientDto
            {
                ProductCode = part.ProductCode,
                ProductName = part.ProductName,
                RequiredQuantity = part.Amount,
                AvailableStock = (double)(ingredientProduct?.Stock.Available ?? 0),
                Image = ingredientProduct?.Image
            };

            ingredients.Add(ingredient);
        }

        giftPackage.Ingredients = ingredients;

        return giftPackage;
    }

    public async Task<GiftPackageManufactureDto> CreateManufactureAsync(
        string giftPackageCode,
        int quantity,
        bool allowStockOverride,
        string requestedByUserName,
        CancellationToken cancellationToken = default)
    {
        // Use provided user name or fallback to current user or "System"
        var userName = !string.IsNullOrEmpty(requestedByUserName) 
            ? requestedByUserName 
            : _currentUserService.GetCurrentUser().Name ?? "System";

        // Create the manufacture log with user name
        var manufactureLog = new GiftPackageManufactureLog(
            giftPackageCode,
            quantity,
            allowStockOverride,
            _timeProvider.GetUtcNow().DateTime,
            userName);

        // Add consumed items - get detailed info with ingredients
        var giftPackage = await GetGiftPackageDetailAsync(giftPackageCode, cancellationToken);

        var stockUpRequest = new StockUpRequest() { StockUpId = Guid.NewGuid().ToString() };
        foreach (var ingredient in giftPackage.Ingredients ?? new List<GiftPackageIngredientDto>())
        {
            var consumedQuantity = (int)(ingredient.RequiredQuantity * quantity);
            manufactureLog.AddConsumedItem(ingredient.ProductCode, consumedQuantity);
            stockUpRequest.Products.Add(new StockUpProductRequest()
            {
                ProductCode = ingredient.ProductCode,
                Amount = consumedQuantity * -1,
            });
        }

        stockUpRequest.Products.Add(new StockUpProductRequest()
        {
            ProductCode = giftPackageCode,
            Amount = quantity,
        });

        await _eshopStockDomainService.StockUpAsync(stockUpRequest);
        // Save to database
        await _giftPackageRepository.AddAsync(manufactureLog);
        await _giftPackageRepository.SaveChangesAsync(cancellationToken);

        return _mapper.Map<GiftPackageManufactureDto>(manufactureLog);
    }

    public Task<string> EnqueueManufactureAsync(
        string giftPackageCode,
        int quantity,
        bool allowStockOverride,
        string requestedByUserName,
        CancellationToken cancellationToken = default)
    {
        var jobId = _backgroundWorker.Enqueue<IGiftPackageManufactureService>(
            service => service.CreateManufactureAsync(giftPackageCode, quantity, allowStockOverride, requestedByUserName, cancellationToken));

        return Task.FromResult(jobId);
    }
}