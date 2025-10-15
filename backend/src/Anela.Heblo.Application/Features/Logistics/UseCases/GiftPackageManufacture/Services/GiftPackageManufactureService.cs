using Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Contracts;
using Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseStockAnalysis;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Logistics.GiftPackageManufacture;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Users;
using Anela.Heblo.Xcc.Services;
using AutoMapper;
using System.ComponentModel;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.GiftPackageManufacture.Services;

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

    public async Task<List<GiftPackageDto>> GetAvailableGiftPackagesAsync(decimal salesCoefficient = 1.0m, DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default)
    {
        // Get all products with ProductType.Set from catalog
        var catalogData = await _catalogRepository.GetAllAsync(cancellationToken);
        var setProducts = catalogData.Where(x => x.Type == ProductType.Set).ToList();

        var giftPackages = new List<GiftPackageDto>();

        // Calculate date range for daily sales calculation
        // Use provided dates or fallback to last 12 months
        var actualToDate = toDate ?? _timeProvider.GetUtcNow().DateTime;
        var actualFromDate = fromDate ?? actualToDate.AddYears(-1);
        var daysDiff = Math.Max((actualToDate - actualFromDate).Days, 1);

        foreach (var product in setProducts)
        {
            // Calculate daily sales from sales history using the actual date range
            var totalSalesInPeriod = (decimal)product.GetTotalSold(actualFromDate, actualToDate) * salesCoefficient;
            var dailySales = totalSalesInPeriod / daysDiff;

            // Calculate suggested quantity: DailySales * OverstockOptimal
            var suggestedQuantity = (int)Math.Max(0, dailySales * product.Properties.OptimalStockDaysSetup);

            // Calculate severity based on new rules
            var severity = CalculateSeverity(
                (int)product.Stock.Available,
                suggestedQuantity,
                (int)product.Properties.StockMinSetup);

            // Calculate stock coverage percentage: (AvailableStock / (DailySales * OverstockOptimal)) * 100
            var stockCoveragePercent = CalculateStockCoveragePercent(
                (int)product.Stock.Available,
                dailySales,
                product.Properties.OptimalStockDaysSetup);

            var giftPackage = new GiftPackageDto
            {
                Code = product.ProductCode,
                Name = product.ProductName,
                AvailableStock = (int)product.Stock.Available,
                DailySales = dailySales,
                OverstockMinimal = (int)product.Properties.StockMinSetup,
                OverstockOptimal = product.Properties.OptimalStockDaysSetup,
                SuggestedQuantity = suggestedQuantity,
                Severity = severity,
                StockCoveragePercent = stockCoveragePercent,
                // Ingredients will be loaded separately when detail is requested
            };

            giftPackages.Add(giftPackage);
        }

        return giftPackages;
    }

    public async Task<GiftPackageDto> GetGiftPackageDetailAsync(string giftPackageCode, decimal salesCoefficient = 1.0m, DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default)
    {
        // Get the basic product info from catalog
        var product = await _catalogRepository.GetByIdAsync(giftPackageCode, cancellationToken);

        if (product == null || product.Type != ProductType.Set)
        {
            throw new ArgumentException($"Gift package '{giftPackageCode}' not found or is not a set product");
        }

        // Calculate date range for daily sales calculation
        // Use provided dates or fallback to last 12 months
        var actualToDate = toDate ?? _timeProvider.GetUtcNow().DateTime;
        var actualFromDate = fromDate ?? actualToDate.AddYears(-1);
        var daysDiff = Math.Max((actualToDate - actualFromDate).Days, 1);

        // Calculate daily sales from sales history using the actual date range
        var totalSalesInPeriod = (decimal)product.GetTotalSold(actualFromDate, actualToDate) * salesCoefficient;
        var dailySales = totalSalesInPeriod / daysDiff;

        // Calculate suggested quantity: DailySales * OverstockOptimal
        var suggestedQuantity = (int)Math.Max(0, dailySales * product.Properties.OptimalStockDaysSetup);

        // Calculate severity based on new rules
        var severity = CalculateSeverity(
            (int)product.Stock.Available,
            suggestedQuantity,
            (int)product.Properties.StockMinSetup);

        // Calculate stock coverage percentage: (AvailableStock / (DailySales * OverstockOptimal)) * 100
        var stockCoveragePercent = CalculateStockCoveragePercent(
            (int)product.Stock.Available,
            dailySales,
            product.Properties.OptimalStockDaysSetup);

        // Create the detailed gift package with ingredients
        var giftPackage = new GiftPackageDto
        {
            Code = product.ProductCode,
            Name = product.ProductName,
            AvailableStock = (int)product.Stock.Available,
            DailySales = dailySales,
            OverstockMinimal = (int)product.Properties.StockMinSetup,
            OverstockOptimal = product.Properties.OptimalStockDaysSetup,
            SuggestedQuantity = suggestedQuantity,
            Severity = severity,
            StockCoveragePercent = stockCoveragePercent,
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

    [DisplayName("GiftPackageManufacture-{0}-{1}")]
    public async Task<GiftPackageManufactureDto> CreateManufactureAsync(
        string giftPackageCode,
        int quantity,
        bool allowStockOverride,
        CancellationToken cancellationToken = default)
    {
        // Create the manufacture log
        var manufactureLog = new GiftPackageManufactureLog(
            giftPackageCode,
            quantity,
            allowStockOverride,
            _timeProvider.GetUtcNow().DateTime,
            _currentUserService.GetCurrentUser().Name ?? "System");

        // Add consumed items - get detailed info with ingredients
        var giftPackage = await GetGiftPackageDetailAsync(giftPackageCode, 1.0m, null, null, cancellationToken);

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

    public async Task<string> EnqueueManufactureAsync(
        string giftPackageCode,
        int quantity,
        bool allowStockOverride = false,
        CancellationToken cancellationToken = default)
    {
        var displayName = $"GiftPackageManufacture-{giftPackageCode}-{quantity}";
        
        var jobId = _backgroundWorker.Enqueue<IGiftPackageManufactureService>(
            service => service.CreateManufactureAsync(giftPackageCode, quantity, allowStockOverride, cancellationToken));

        return jobId;
    }

    private static StockSeverity CalculateSeverity(int availableStock, int suggestedQuantity, int overstockMinimal)
    {
        // If less than minimal (in any case), then severity is Critical (red on UI)
        if (availableStock < overstockMinimal)
        {
            return StockSeverity.Critical;
        }

        // If suggestedQuantity < availableStock but greater than overstockMinimal, then severity is Severe (orange on UI)
        if (availableStock < suggestedQuantity)
        {
            return StockSeverity.Severe;
        }

        return StockSeverity.Optimal;
    }

    private static decimal CalculateStockCoveragePercent(int availableStock, decimal dailySales, int overstockOptimal)
    {
        // Calculate stock coverage percentage: (AvailableStock / (DailySales * OverstockOptimal)) * 100
        // If dailySales is 0, return 0% to avoid division by zero
        if (dailySales <= 0 || overstockOptimal <= 0)
        {
            return 0m;
        }

        var optimalStockAmount = dailySales * overstockOptimal;
        return (availableStock / optimalStockAmount) * 100m;
    }
}