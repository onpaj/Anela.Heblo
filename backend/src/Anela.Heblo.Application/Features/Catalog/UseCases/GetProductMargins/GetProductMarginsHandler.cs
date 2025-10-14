using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Features.Catalog.Infrastructure.Exceptions;
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Accounting.Ledger;
using Anela.Heblo.Domain.Features.Catalog;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetProductMargins;

public class GetProductMarginsHandler : IRequestHandler<GetProductMarginsRequest, GetProductMarginsResponse>
{
    private readonly ICatalogRepository _catalogRepository;
    private readonly ILogger<GetProductMarginsHandler> _logger;

    public GetProductMarginsHandler(
        ICatalogRepository catalogRepository,
        ILogger<GetProductMarginsHandler> logger
        )
    {
        _catalogRepository = catalogRepository;
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
                var allDtos = CreateMargins(filteredItems);
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
                items = CreateMargins(pagedItems);
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

    private List<ProductMarginDto> CreateMargins(List<CatalogAggregate> products)
    {
        var results = new List<ProductMarginDto>();

        foreach (var product in products)
        {
            try
            {
                var dto = MapToMarginDto(product);
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
                    ManufactureDifficulty = 0,
                    M0 = new MarginLevelDto(),
                    M1 = new MarginLevelDto(),
                    M2 = new MarginLevelDto(),
                    M3 = new MarginLevelDto()
                });
            }
        }

        return results;
    }

    private ProductMarginDto MapToMarginDto(CatalogAggregate product)
    {
        try
        {
            // Use pre-calculated margin data from CatalogAggregate.Margins
            var marginHistory = product.Margins;

            // Filter monthly history to last 13 months
            var dateFrom = DateTime.Now.AddMonths(-13);
            var filteredMonthlyData = marginHistory.MonthlyData
                .Where(m => m.Month >= dateFrom)
                .ToList();

            var dto = new ProductMarginDto
            {
                ProductCode = product?.ProductCode ?? "UNKNOWN",
                ProductName = product?.ProductName ?? "Unknown Product",
                ManufactureDifficulty = product?.ManufactureDifficulty ?? 0,

                // Use pre-calculated averages from margin history
                M0 = new MarginLevelDto
                {
                    Percentage = marginHistory.Averages.M0.Percentage,
                    Amount = marginHistory.Averages.M0.Amount,
                    CostLevel = marginHistory.Averages.M0.CostLevel,
                    CostTotal = marginHistory.Averages.M0.CostTotal
                },
                M1 = new MarginLevelDto
                {
                    Percentage = marginHistory.Averages.M1.Percentage,
                    Amount = marginHistory.Averages.M1.Amount,
                    CostLevel = marginHistory.Averages.M1.CostLevel,
                    CostTotal = marginHistory.Averages.M1.CostTotal
                },
                M2 = new MarginLevelDto
                {
                    Percentage = marginHistory.Averages.M2.Percentage,
                    Amount = marginHistory.Averages.M2.Amount,
                    CostLevel = marginHistory.Averages.M2.CostLevel,
                    CostTotal = marginHistory.Averages.M2.CostTotal
                },
                M3 = new MarginLevelDto
                {
                    Percentage = marginHistory.Averages.M3.Percentage,
                    Amount = marginHistory.Averages.M3.Amount,
                    CostLevel = marginHistory.Averages.M3.CostLevel,
                    CostTotal = marginHistory.Averages.M3.CostTotal
                },

                // Monthly history for charts (filtered to last 13 months)
                MonthlyHistory = filteredMonthlyData.Select(m => new MonthlyMarginDto
                {
                    Month = m.Month,
                    M0 = new MarginLevelDto
                    {
                        Percentage = m.M0.Percentage,
                        Amount = m.M0.Amount,
                        CostLevel = m.M0.CostLevel,
                        CostTotal = m.M0.CostTotal
                    },
                    M1 = new MarginLevelDto
                    {
                        Percentage = m.M1.Percentage,
                        Amount = m.M1.Amount,
                        CostLevel = m.M1.CostLevel,
                        CostTotal = m.M1.CostTotal
                    },
                    M2 = new MarginLevelDto
                    {
                        Percentage = m.M2.Percentage,
                        Amount = m.M2.Amount,
                        CostLevel = m.M2.CostLevel,
                        CostTotal = m.M2.CostTotal
                    },
                    M3 = new MarginLevelDto
                    {
                        Percentage = m.M3.Percentage,
                        Amount = m.M3.Amount,
                        CostLevel = m.M3.CostLevel,
                        CostTotal = m.M3.CostTotal
                    }
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
            "m2percentage" or "m2amount" or "m3percentage" or "m3amount" => true,
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
                ? items.OrderByDescending(x => x.M0.Amount).ToList()
                : items.OrderBy(x => x.M0.Amount).ToList(),
            "m1amount" => sortDescending
                ? items.OrderByDescending(x => x.M1.Amount).ToList()
                : items.OrderBy(x => x.M1.Amount).ToList(),
            "m2amount" => sortDescending
                ? items.OrderByDescending(x => x.M2.Amount).ToList()
                : items.OrderBy(x => x.M2.Amount).ToList(),
            "m3amount" => sortDescending
                ? items.OrderByDescending(x => x.M3.Amount).ToList()
                : items.OrderBy(x => x.M3.Amount).ToList(),
            // M0-M3 margin levels - percentages
            "m0percentage" => sortDescending
                ? items.OrderByDescending(x => x.M0.Percentage).ToList()
                : items.OrderBy(x => x.M0.Percentage).ToList(),
            "m1percentage" => sortDescending
                ? items.OrderByDescending(x => x.M1.Percentage).ToList()
                : items.OrderBy(x => x.M1.Percentage).ToList(),
            "m2percentage" => sortDescending
                ? items.OrderByDescending(x => x.M2.Percentage).ToList()
                : items.OrderBy(x => x.M2.Percentage).ToList(),
            "m3percentage" => sortDescending
                ? items.OrderByDescending(x => x.M3.Percentage).ToList()
                : items.OrderBy(x => x.M3.Percentage).ToList(),
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