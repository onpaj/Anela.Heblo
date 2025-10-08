using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Features.Catalog.Infrastructure.Exceptions;
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Accounting.Ledger;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Services;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetProductMargins;

public class GetProductMarginsHandler : IRequestHandler<GetProductMarginsRequest, GetProductMarginsResponse>
{
    private readonly ICatalogRepository _catalogRepository;
    private readonly IMarginCalculationService _marginCalculationService;
    private readonly ILogger<GetProductMarginsHandler> _logger;

    public GetProductMarginsHandler(
        ICatalogRepository catalogRepository,
        IMarginCalculationService marginCalculationService,
        ILogger<GetProductMarginsHandler> logger
        )
    {
        _catalogRepository = catalogRepository;
        _marginCalculationService = marginCalculationService;
        _logger = logger;
    }

    public async Task<GetProductMarginsResponse> Handle(GetProductMarginsRequest request, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Starting product margins query with filters: ProductCode={ProductCode}, ProductName={ProductName}, ProductType={ProductType}",
                request.ProductCode, request.ProductName, request.ProductType);

            var allItems = await GetProducts(cancellationToken);
            var query = allItems.AsQueryable();

            query = ApplyFilters(query, request);

            // Convert to list first to enable in-memory operations for complex sorting
            var filteredItems = query.ToList();
            var totalCount = filteredItems.Count;

            // Check if sorting requires DTO calculation (M0-M3 margins)
            bool requiresComplexSorting = RequiresComplexSorting(request.SortBy);

            List<ProductMarginDto> items;

            if (requiresComplexSorting)
            {
                // For complex sorting (M0-M3), we need to create all DTOs first, then sort
                var allDtos = await CreateMarginsAsync(filteredItems, cancellationToken);
                var sortedDtos = ApplyComplexSorting(allDtos, request.SortBy, request.SortDescending);

                // Apply pagination after sorting
                items = sortedDtos
                    .Skip((request.PageNumber - 1) * request.PageSize)
                    .Take(request.PageSize)
                    .ToList();
            }
            else
            {
                // For basic sorting, sort entities first for better performance
                var sortedFilteredItems = ApplyBasicSortingOnEntities(filteredItems, request.SortBy, request.SortDescending);

                // Apply pagination to reduce number of expensive DTO mappings
                var pagedItems = sortedFilteredItems
                    .Skip((request.PageNumber - 1) * request.PageSize)
                    .Take(request.PageSize)
                    .ToList();

                // Create DTOs only for the paged items (performance optimization)
                items = await CreateMarginsAsync(pagedItems, cancellationToken);
            }

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
            return new GetProductMarginsResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.MarginCalculationError,
                Params = new Dictionary<string, string> { { "details", ex.Message } }
            };
        }
    }

    private async Task<List<CatalogAggregate>> GetProducts(CancellationToken cancellationToken)
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
            throw; // Re-throw to be caught by the main handler
        }
    }

    private IQueryable<CatalogAggregate> ApplyFilters(IQueryable<CatalogAggregate> query, GetProductMarginsRequest request)
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
            throw; // Re-throw to be caught by the main handler
        }
    }

    private async Task<List<ProductMarginDto>> CreateMarginsAsync(List<CatalogAggregate> products, CancellationToken cancellationToken)
    {
        var results = new List<ProductMarginDto>();

        foreach (var product in products)
        {
            try
            {
                var dto = await MapToMarginDtoAsync(product, cancellationToken);
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
                    AverageSalesCost = null,
                    AverageOverheadCost = null,
                    ManufactureDifficulty = 0,
                    M0Percentage = 0,
                    M0Amount = 0,
                    M1Percentage = 0,
                    M1Amount = 0,
                    M2Percentage = 0,
                    M2Amount = 0,
                    M3Percentage = 0,
                    M3Amount = 0
                });
            }
        }

        return results;
    }

    private async Task<ProductMarginDto> MapToMarginDtoAsync(CatalogAggregate product, CancellationToken cancellationToken)
    {
        try
        {
            // Calculate margins for the last 13 months using the domain service
            var dateFrom = DateOnly.FromDateTime(DateTime.Now.AddMonths(-13));
            var dateTo = DateOnly.FromDateTime(DateTime.Now);
            var monthlyHistory = await _marginCalculationService.GetMarginAsync(product, dateFrom, dateTo, cancellationToken);

            var dto = new ProductMarginDto
            {
                ProductCode = product?.ProductCode ?? "UNKNOWN",
                ProductName = product?.ProductName ?? "Unknown Product",
                ManufactureDifficulty = product?.ManufactureDifficulty ?? 0,

                // Legacy margin properties removed - using M0-M3 levels instead

                // Average month M0-M3 margins
                M0Percentage = monthlyHistory.Averages.M0.Percentage,
                M0Amount = monthlyHistory.Averages.M0.Amount,
                M1Percentage = monthlyHistory.Averages.M1.Percentage,
                M1Amount = monthlyHistory.Averages.M1.Amount,
                M2Percentage = monthlyHistory.Averages.M2.Percentage,
                M2Amount = monthlyHistory.Averages.M2.Amount,
                M3Percentage = monthlyHistory.Averages.M3.Percentage,
                M3Amount = monthlyHistory.Averages.M3.Amount,

                // Cost components are now calculated below as average fields to avoid duplication

                // Monthly history for charts
                MonthlyHistory = monthlyHistory.MonthlyData.Select(m => new MonthlyMarginDto
                {
                    Month = m.Month,
                    M0Percentage = m.M0.Percentage,
                    M1Percentage = m.M1.Percentage,
                    M2Percentage = m.M2.Percentage,
                    M3Percentage = m.M3.Percentage,
                    MaterialCost = m.CostsForMonth.MaterialCost,
                    ManufacturingCost = m.CostsForMonth.ManufacturingCost,
                    SalesCost = m.CostsForMonth.SalesCost,
                    TotalCosts = m.CostsForMonth.M3Cost
                }).ToList()
            };

            dto.PriceWithoutVat = product?.PriceWithoutVat;
            dto.PriceWithoutVatIsFromEshop = product?.PriceIsFromEshop ?? false;

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

            // Calculate average costs from historical data (excluding zero values) for frontend use
            dto.AverageMaterialCost = monthlyHistory.MonthlyData.Where(w => w.CostsForMonth.MaterialCost > 0).DefaultIfEmpty().Average(a => a?.CostsForMonth.MaterialCost ?? 0);
            dto.AverageHandlingCost = monthlyHistory.MonthlyData.Where(w => w.CostsForMonth.ManufacturingCost > 0).DefaultIfEmpty().Average(a => a?.CostsForMonth.ManufacturingCost ?? 0);
            dto.AverageSalesCost = monthlyHistory.MonthlyData.Where(w => w.CostsForMonth.SalesCost > 0).DefaultIfEmpty().Average(a => a?.CostsForMonth.SalesCost ?? 0);
            dto.AverageOverheadCost = monthlyHistory.MonthlyData.Where(w => w.CostsForMonth.OverheadCost > 0).DefaultIfEmpty().Average(a => a?.CostsForMonth.OverheadCost ?? 0);

            return dto;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error mapping product {ProductCode} to margin DTO", product?.ProductCode);
            throw; // Re-throw to be caught by the main handler
        }
    }


    private static bool RequiresComplexSorting(string? sortBy)
    {
        if (string.IsNullOrWhiteSpace(sortBy))
            return false;

        return sortBy.ToLower() switch
        {
            "m0percentage" or "m0amount" or "m1percentage" or "m1amount" or
            "m2percentage" or "m2amount" or "m3percentage" or "m3amount" or
            "averagematerialcost" or "averagehandlingcost" or "averagesalescost" or "averageoverheadcost" => true,
            _ => false
        };
    }

    private static List<ProductMarginDto> ApplyComplexSorting(List<ProductMarginDto> items, string? sortBy, bool sortDescending)
    {
        if (string.IsNullOrWhiteSpace(sortBy))
        {
            // Default sorting by ProductCode
            return sortDescending
                ? items.OrderByDescending(x => x.ProductCode).ToList()
                : items.OrderBy(x => x.ProductCode).ToList();
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
            "manufacturedifficulty" => sortDescending
                ? items.OrderByDescending(x => x.ManufactureDifficulty).ToList()
                : items.OrderBy(x => x.ManufactureDifficulty).ToList(),
            // M0-M3 margin levels - amounts
            "m0amount" => sortDescending
                ? items.OrderByDescending(x => x.M0Amount).ToList()
                : items.OrderBy(x => x.M0Amount).ToList(),
            "m1amount" => sortDescending
                ? items.OrderByDescending(x => x.M1Amount).ToList()
                : items.OrderBy(x => x.M1Amount).ToList(),
            "m2amount" => sortDescending
                ? items.OrderByDescending(x => x.M2Amount).ToList()
                : items.OrderBy(x => x.M2Amount).ToList(),
            "m3amount" => sortDescending
                ? items.OrderByDescending(x => x.M3Amount).ToList()
                : items.OrderBy(x => x.M3Amount).ToList(),
            // M0-M3 margin levels - percentages
            "m0percentage" => sortDescending
                ? items.OrderByDescending(x => x.M0Percentage).ToList()
                : items.OrderBy(x => x.M0Percentage).ToList(),
            "m1percentage" => sortDescending
                ? items.OrderByDescending(x => x.M1Percentage).ToList()
                : items.OrderBy(x => x.M1Percentage).ToList(),
            "m2percentage" => sortDescending
                ? items.OrderByDescending(x => x.M2Percentage).ToList()
                : items.OrderBy(x => x.M2Percentage).ToList(),
            "m3percentage" => sortDescending
                ? items.OrderByDescending(x => x.M3Percentage).ToList()
                : items.OrderBy(x => x.M3Percentage).ToList(),
            // Cost components
            "averagematerialcost" => sortDescending
                ? items.OrderByDescending(x => x.AverageMaterialCost ?? 0).ToList()
                : items.OrderBy(x => x.AverageMaterialCost ?? 0).ToList(),
            "averagehandlingcost" => sortDescending
                ? items.OrderByDescending(x => x.AverageHandlingCost ?? 0).ToList()
                : items.OrderBy(x => x.AverageHandlingCost ?? 0).ToList(),
            "averagesalescost" => sortDescending
                ? items.OrderByDescending(x => x.AverageSalesCost ?? 0).ToList()
                : items.OrderBy(x => x.AverageSalesCost ?? 0).ToList(),
            "averageoverheadcost" => sortDescending
                ? items.OrderByDescending(x => x.AverageOverheadCost ?? 0).ToList()
                : items.OrderBy(x => x.AverageOverheadCost ?? 0).ToList(),
            _ => sortDescending
                ? items.OrderByDescending(x => x.ProductCode).ToList()
                : items.OrderBy(x => x.ProductCode).ToList()
        };
    }

    private static List<CatalogAggregate> ApplyBasicSortingOnEntities(List<CatalogAggregate> items, string? sortBy, bool sortDescending)
    {
        if (string.IsNullOrWhiteSpace(sortBy))
        {
            // Default sorting by ProductCode (since legacy MarginPercentage is removed)
            return sortDescending
                ? items.OrderByDescending(x => x.ProductCode).ToList()
                : items.OrderBy(x => x.ProductCode).ToList();
        }

        return sortBy.ToLower() switch
        {
            "productcode" => sortDescending
                ? items.OrderByDescending(x => x.ProductCode).ToList()
                : items.OrderBy(x => x.ProductCode).ToList(),
            "productname" => sortDescending
                ? items.OrderByDescending(x => x.ProductName).ToList()
                : items.OrderBy(x => x.ProductName).ToList(),
            "pricewithoutvat" => sortDescending
                ? items.OrderByDescending(x => x.PriceWithoutVat ?? 0).ToList()
                : items.OrderBy(x => x.PriceWithoutVat ?? 0).ToList(),
            "purchaseprice" => sortDescending
                ? items.OrderByDescending(x => x.ErpPrice?.PurchasePrice ?? 0).ToList()
                : items.OrderBy(x => x.ErpPrice?.PurchasePrice ?? 0).ToList(),
            "manufacturedifficulty" => sortDescending
                ? items.OrderByDescending(x => x.ManufactureDifficulty ?? 0).ToList()
                : items.OrderBy(x => x.ManufactureDifficulty ?? 0).ToList(),
            _ => sortDescending
                ? items.OrderByDescending(x => x.ProductCode).ToList()
                : items.OrderBy(x => x.ProductCode).ToList()
        };
    }
}