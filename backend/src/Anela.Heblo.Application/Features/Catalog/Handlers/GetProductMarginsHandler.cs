using MediatR;
using Microsoft.Extensions.Logging;
using Anela.Heblo.Application.Features.Catalog.Model;
using Anela.Heblo.Application.Features.Catalog.Exceptions;
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Domain.Accounting.Ledger;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Manufacture;

namespace Anela.Heblo.Application.Features.Catalog;

public class GetProductMarginsHandler : IRequestHandler<GetProductMarginsRequest, GetProductMarginsResponse>
{
    private readonly ICatalogRepository _catalogRepository;
    private readonly ILedgerService _ledgerService;
    private readonly TimeProvider _timeProvider;
    private readonly SafeMarginCalculator _marginCalculator;
    private readonly ILogger<GetProductMarginsHandler> _logger;

    public GetProductMarginsHandler(
        ICatalogRepository catalogRepository,
        ILedgerService ledgerService,
        TimeProvider timeProvider,
        SafeMarginCalculator marginCalculator,
        ILogger<GetProductMarginsHandler> logger
        )
    {
        _catalogRepository = catalogRepository;
        _ledgerService = ledgerService;
        _timeProvider = timeProvider;
        _marginCalculator = marginCalculator;
        _logger = logger;
    }

    public async Task<GetProductMarginsResponse> Handle(GetProductMarginsRequest request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Starting product margins query with filters: ProductCode={ProductCode}, ProductName={ProductName}, ProductType={ProductType}",
                request.ProductCode, request.ProductName, request.ProductType);

            var allItems = await GetProductsWithErrorHandling(cancellationToken);
            var query = allItems.AsQueryable();

            query = ApplyFiltersWithErrorHandling(query, request);

            // Convert to list first to enable in-memory operations for complex sorting
            var filteredItems = query.ToList();
            var totalCount = filteredItems.Count;

            // Create DTOs with calculated values and error handling
            var itemsWithCalculatedValues = CreateMarginsWithErrorHandling(filteredItems);

            // Apply sorting in memory
            itemsWithCalculatedValues = ApplySortingInMemory(itemsWithCalculatedValues, request.SortBy, request.SortDescending);

            // Apply pagination
            var items = itemsWithCalculatedValues
                .Skip((request.PageNumber - 1) * request.PageSize)
                .Take(request.PageSize)
                .ToList();

            _logger.LogDebug("Product margins query completed successfully. Found {Count} products, returning {PageSize} items",
                totalCount, items.Count);

            return new GetProductMarginsResponse
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = request.PageNumber,
                PageSize = request.PageSize
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing product margins query");
            throw new ProductMarginsException("Failed to retrieve product margins", ex);
        }
    }

    private async Task<List<CatalogAggregate>> GetProductsWithErrorHandling(CancellationToken cancellationToken)
    {
        try
        {
            var products = await _catalogRepository.GetAllAsync(cancellationToken);
            if (products == null)
            {
                _logger.LogWarning("Catalog repository returned null product list");
                return new List<CatalogAggregate>();
            }

            return products.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve products from catalog repository");
            throw new DataAccessException("Unable to access product catalog", ex);
        }
    }

    private IQueryable<CatalogAggregate> ApplyFiltersWithErrorHandling(IQueryable<CatalogAggregate> query, GetProductMarginsRequest request)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(request.ProductCode))
            {
                query = query.Where(x => x.ProductCode != null && x.ProductCode.Contains(request.ProductCode, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(request.ProductName))
            {
                query = query.Where(x => x.ProductName != null && x.ProductName.Contains(request.ProductName, StringComparison.OrdinalIgnoreCase));
            }

            if (request.ProductType.HasValue)
            {
                query = query.Where(x => x.Type == request.ProductType.Value);
            }
            else
            {
                // Default filter: only Product and Goods
                query = query.Where(x => x.Type == ProductType.Product || x.Type == ProductType.Goods);
            }

            return query;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying filters to product query");
            throw new ProductMarginsException("Failed to apply product filters", ex);
        }
    }

    private List<ProductMarginDto> CreateMarginsWithErrorHandling(List<CatalogAggregate> products)
    {
        var results = new List<ProductMarginDto>();

        foreach (var product in products)
        {
            try
            {
                var dto = MapToMarginDtoSafely(product);
                results.Add(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing product {ProductCode} - {ProductName}. Skipping product.",
                    product?.ProductCode, product?.ProductName);

                // Add error placeholder to maintain count consistency
                results.Add(new ProductMarginDto
                {
                    ProductCode = product?.ProductCode ?? "ERROR",
                    ProductName = $"Error processing product: {ex.Message}",
                    PriceWithoutVat = null,
                    PurchasePrice = null,
                    AverageMaterialCost = null,
                    AverageHandlingCost = null,
                    ManufactureDifficulty = 0,
                    MarginPercentage = 0,
                    MarginAmount = 0
                });
            }
        }

        return results;
    }

    private ProductMarginDto MapToMarginDtoSafely(CatalogAggregate product)
    {
        try
        {
            var dto = new ProductMarginDto
            {
                ProductCode = product?.ProductCode ?? "UNKNOWN",
                ProductName = product?.ProductName ?? "Unknown Product",
                ManufactureDifficulty = product?.ManufactureDifficulty ?? 0,
                MarginPercentage = product?.MarginPercentage ?? 0,
                MarginAmount = product?.MarginAmount ?? 0
            };

            // Safe price extraction
            if (product?.EshopPrice?.PriceWithoutVat != null)
            {
                dto.PriceWithoutVat = product.EshopPrice.PriceWithoutVat;
            }
            else
            {
                _logger.LogDebug("Product {ProductCode} missing e-shop price", product?.ProductCode);
                dto.PriceWithoutVat = null;
            }

            // Safe cost extraction
            if (product?.ErpPrice?.PurchasePrice != null)
            {
                dto.PurchasePrice = product.ErpPrice.PurchasePrice;
            }
            else
            {
                _logger.LogDebug("Product {ProductCode} missing ERP purchase price", product?.ProductCode);
                dto.PurchasePrice = null;
            }

            // Safe material cost calculation
            try
            {
                dto.AverageMaterialCost = CalculateAverageMaterialCostFromHistory(product?.ManufactureCostHistory ?? new List<ManufactureCost>());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to calculate average material cost for product {ProductCode}", product?.ProductCode);
                dto.AverageMaterialCost = null;
            }

            // Safe handling cost calculation
            try
            {
                dto.AverageHandlingCost = CalculateAverageHandlingCostFromHistory(product?.ManufactureCostHistory ?? new List<ManufactureCost>());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to calculate average handling cost for product {ProductCode}", product?.ProductCode);
                dto.AverageHandlingCost = null;
            }

            return dto;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error mapping product {ProductCode} to margin DTO", product?.ProductCode);
            throw new MarginCalculationException($"Failed to create margin DTO for product {product?.ProductCode}", ex);
        }
    }

    private static decimal? CalculateAverageMaterialCostFromHistory(List<ManufactureCost> manufactureCostHistory)
    {
        if (manufactureCostHistory == null || manufactureCostHistory.Count == 0)
        {
            return null;
        }

        // Exclude zero values as requested
        var nonZeroCosts = manufactureCostHistory
            .Where(c => c.MaterialCost > 0)
            .ToList();

        if (nonZeroCosts.Count == 0)
        {
            return null;
        }

        return nonZeroCosts.Average(c => c.MaterialCost);
    }

    private static decimal? CalculateAverageHandlingCostFromHistory(List<ManufactureCost> manufactureCostHistory)
    {
        if (manufactureCostHistory == null || manufactureCostHistory.Count == 0)
        {
            return null;
        }

        // Exclude zero values as requested
        var nonZeroCosts = manufactureCostHistory
            .Where(c => c.HandlingCost > 0)
            .ToList();

        if (nonZeroCosts.Count == 0)
        {
            return null;
        }

        return nonZeroCosts.Average(c => c.HandlingCost);
    }

    private static List<ProductMarginDto> ApplySortingInMemory(List<ProductMarginDto> items, string? sortBy, bool sortDescending)
    {
        if (string.IsNullOrWhiteSpace(sortBy))
        {
            // Default sorting by MarginPercentage descending (highest margins first)
            return sortDescending
                ? items.OrderByDescending(x => x.MarginPercentage).ToList()
                : items.OrderBy(x => x.MarginPercentage).ToList();
        }

        return sortBy.ToLower() switch
        {
            "productcode" => sortDescending
                ? items.OrderByDescending(x => x.ProductCode).ToList()
                : items.OrderBy(x => x.ProductCode).ToList(),
            "productname" => sortDescending
                ? items.OrderByDescending(x => x.ProductName).ToList()
                : items.OrderBy(x => x.ProductName).ToList(),
            "pricewithoutVat" => sortDescending
                ? items.OrderByDescending(x => x.PriceWithoutVat ?? 0).ToList()
                : items.OrderBy(x => x.PriceWithoutVat ?? 0).ToList(),
            "purchaseprice" => sortDescending
                ? items.OrderByDescending(x => x.PurchasePrice ?? 0).ToList()
                : items.OrderBy(x => x.PurchasePrice ?? 0).ToList(),
            "averagematerialcost" => sortDescending
                ? items.OrderByDescending(x => x.AverageMaterialCost ?? 0).ToList()
                : items.OrderBy(x => x.AverageMaterialCost ?? 0).ToList(),
            "averagehandlingcost" => sortDescending
                ? items.OrderByDescending(x => x.AverageHandlingCost ?? 0).ToList()
                : items.OrderBy(x => x.AverageHandlingCost ?? 0).ToList(),
            "marginpercentage" => sortDescending
                ? items.OrderByDescending(x => x.MarginPercentage).ToList()
                : items.OrderBy(x => x.MarginPercentage).ToList(),
            _ => sortDescending
                ? items.OrderByDescending(x => x.MarginPercentage).ToList()
                : items.OrderBy(x => x.MarginPercentage).ToList()
        };
    }
}