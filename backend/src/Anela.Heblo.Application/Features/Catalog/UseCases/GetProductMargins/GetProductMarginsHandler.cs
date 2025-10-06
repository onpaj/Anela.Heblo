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

            // Create DTOs with calculated values and error handling
            var itemsWithCalculatedValues = await CreateMarginsAsync(filteredItems, cancellationToken);

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
                    ManufactureDifficulty = 0,
                    MarginPercentage = 0,
                    MarginAmount = 0
                });
            }
        }

        return results;
    }

    private async Task<ProductMarginDto> MapToMarginDtoAsync(CatalogAggregate product, CancellationToken cancellationToken)
    {
        try
        {
            // Calculate margins using the domain service
            var marginResult = await _marginCalculationService.CalculateAllMarginLevelsAsync(product, cancellationToken);
            var monthlyHistory = await _marginCalculationService.CalculateMonthlyMarginHistoryAsync(product, 13, cancellationToken);

            var dto = new ProductMarginDto
            {
                ProductCode = product?.ProductCode ?? "UNKNOWN",
                ProductName = product?.ProductName ?? "Unknown Product",
                ManufactureDifficulty = product?.ManufactureDifficulty ?? 0,

                // Keep existing margin calculation for backward compatibility
                MarginPercentage = product?.MarginPercentage ?? 0,
                MarginAmount = product?.MarginAmount ?? 0,

                // Current month M0-M3 margins
                M0Percentage = marginResult.M0.Percentage,
                M0Amount = marginResult.M0.Amount,
                M1Percentage = marginResult.M1.Percentage,
                M1Amount = marginResult.M1.Amount,
                M2Percentage = marginResult.M2.Percentage,
                M2Amount = marginResult.M2.Amount,
                M3Percentage = marginResult.M3.Percentage,
                M3Amount = marginResult.M3.Amount,

                // 12-month averages
                M0PercentageAvg12M = marginResult.M0Average12Months.Percentage,
                M1PercentageAvg12M = marginResult.M1Average12Months.Percentage,
                M2PercentageAvg12M = marginResult.M2Average12Months.Percentage,
                M3PercentageAvg12M = marginResult.M3Average12Months.Percentage,

                // Cost components for tooltips
                MaterialCost = marginResult.CostBreakdown.MaterialCost > 0 ? marginResult.CostBreakdown.MaterialCost : null,
                ManufacturingCost = marginResult.CostBreakdown.ManufacturingCost > 0 ? marginResult.CostBreakdown.ManufacturingCost : null,
                SalesCost = marginResult.CostBreakdown.SalesCost > 0 ? marginResult.CostBreakdown.SalesCost : null,
                TotalCosts = marginResult.CostBreakdown.M3Cost > 0 ? marginResult.CostBreakdown.M3Cost : null,

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

            // Use cost breakdown from margin calculation service for backward compatibility
            dto.AverageMaterialCost = marginResult.CostBreakdown.MaterialCost > 0 ? marginResult.CostBreakdown.MaterialCost : null;
            dto.AverageHandlingCost = marginResult.CostBreakdown.ManufacturingCost > 0 ? marginResult.CostBreakdown.ManufacturingCost : null;

            return dto;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error mapping product {ProductCode} to margin DTO", product?.ProductCode);
            throw; // Re-throw to be caught by the main handler
        }
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
            "manufacturedifficulty" => sortDescending
                ? items.OrderByDescending(x => x.ManufactureDifficulty).ToList()
                : items.OrderBy(x => x.ManufactureDifficulty).ToList(),
            "marginpercentage" => sortDescending
                ? items.OrderByDescending(x => x.MarginPercentage).ToList()
                : items.OrderBy(x => x.MarginPercentage).ToList(),
            _ => sortDescending
                ? items.OrderByDescending(x => x.MarginPercentage).ToList()
                : items.OrderBy(x => x.MarginPercentage).ToList()
        };
    }
}