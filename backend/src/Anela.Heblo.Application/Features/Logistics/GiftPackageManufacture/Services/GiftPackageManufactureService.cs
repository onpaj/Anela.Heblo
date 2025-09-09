using Anela.Heblo.Application.Features.Logistics.GiftPackageManufacture.Contracts;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Logistics.GiftPackageManufacture;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Users;
using AutoMapper;

namespace Anela.Heblo.Application.Features.Logistics.GiftPackageManufacture.Services;

public class GiftPackageManufactureService : IGiftPackageManufactureService
{
    private readonly IManufactureRepository _manufactureRepository;
    private readonly IGiftPackageManufactureRepository _giftPackageRepository;
    private readonly ICatalogRepository _catalogRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IMapper _mapper;
    private readonly TimeProvider _timeProvider;

    public GiftPackageManufactureService(
        IManufactureRepository manufactureRepository,
        IGiftPackageManufactureRepository giftPackageRepository,
        ICatalogRepository catalogRepository,
        ICurrentUserService currentUserService,
        IMapper mapper,
        TimeProvider timeProvider)
    {
        _manufactureRepository = manufactureRepository;
        _giftPackageRepository = giftPackageRepository;
        _catalogRepository = catalogRepository;
        _currentUserService = currentUserService;
        _mapper = mapper;
        _timeProvider = timeProvider;
    }

    public async Task<List<GiftPackageDto>> GetAvailableGiftPackagesAsync(CancellationToken cancellationToken = default)
    {
        // For MVP, we'll return a hardcoded list of gift packages
        // In the future, this would query ABRA for actual gift package BOMs
        var giftPackages = new List<GiftPackageDto>
        {
            new GiftPackageDto
            {
                Code = "GIFT001",
                Name = "Basic Gift Package",
                Ingredients = new List<GiftPackageIngredientDto>
                {
                    new() { ProductCode = "PROD001", ProductName = "Product 1", RequiredQuantity = 2, AvailableStock = 10 },
                    new() { ProductCode = "PROD002", ProductName = "Product 2", RequiredQuantity = 1, AvailableStock = 5 }
                }
            },
            new GiftPackageDto
            {
                Code = "GIFT002",
                Name = "Premium Gift Package",
                Ingredients = new List<GiftPackageIngredientDto>
                {
                    new() { ProductCode = "PROD003", ProductName = "Product 3", RequiredQuantity = 1, AvailableStock = 3 },
                    new() { ProductCode = "PROD004", ProductName = "Product 4", RequiredQuantity = 2, AvailableStock = 8 },
                    new() { ProductCode = "PROD005", ProductName = "Product 5", RequiredQuantity = 1, AvailableStock = 2 }
                }
            }
        };

        return giftPackages;
    }

    public async Task<GiftPackageStockValidationDto> ValidateStockAsync(string giftPackageCode, int quantity, CancellationToken cancellationToken = default)
    {
        var giftPackages = await GetAvailableGiftPackagesAsync(cancellationToken);
        var giftPackage = giftPackages.FirstOrDefault(x => x.Code == giftPackageCode);

        if (giftPackage == null)
        {
            throw new ArgumentException($"Gift package '{giftPackageCode}' not found");
        }

        var shortages = new List<StockShortageDto>();
        var hasSufficientStock = true;

        foreach (var ingredient in giftPackage.Ingredients)
        {
            var requiredQuantity = ingredient.RequiredQuantity * quantity;
            
            if (ingredient.AvailableStock < requiredQuantity)
            {
                hasSufficientStock = false;
                shortages.Add(new StockShortageDto
                {
                    ProductCode = ingredient.ProductCode,
                    ProductName = ingredient.ProductName,
                    RequiredQuantity = requiredQuantity,
                    AvailableStock = ingredient.AvailableStock
                });
            }
        }

        return new GiftPackageStockValidationDto
        {
            GiftPackageCode = giftPackageCode,
            RequestedQuantity = quantity,
            HasSufficientStock = hasSufficientStock,
            Shortages = shortages
        };
    }

    public async Task<GiftPackageManufactureDto> CreateManufactureAsync(
        string giftPackageCode, 
        int quantity, 
        bool allowStockOverride, 
        Guid userId, 
        CancellationToken cancellationToken = default)
    {
        // Validate stock first
        var validation = await ValidateStockAsync(giftPackageCode, quantity, cancellationToken);
        
        if (!validation.HasSufficientStock && !allowStockOverride)
        {
            throw new InvalidOperationException("Insufficient stock for manufacturing. Use stock override to proceed.");
        }

        // Create the manufacture log
        var manufactureLog = new GiftPackageManufactureLog(
            giftPackageCode,
            quantity,
            !validation.HasSufficientStock,
            _timeProvider.GetUtcNow().DateTime,
            userId);

        // Add consumed items
        var giftPackages = await GetAvailableGiftPackagesAsync(cancellationToken);
        var giftPackage = giftPackages.First(x => x.Code == giftPackageCode);
        
        foreach (var ingredient in giftPackage.Ingredients)
        {
            var consumedQuantity = (int)(ingredient.RequiredQuantity * quantity);
            manufactureLog.AddConsumedItem(ingredient.ProductCode, consumedQuantity);
        }

        // Save to database
        await _giftPackageRepository.AddAsync(manufactureLog);
        await _giftPackageRepository.SaveChangesAsync(cancellationToken);

        // TODO: Integrate with catalog module to update stock levels
        // This would involve calling stock correction APIs to subtract consumed products
        // and add created gift packages to inventory

        return _mapper.Map<GiftPackageManufactureDto>(manufactureLog);
    }
}