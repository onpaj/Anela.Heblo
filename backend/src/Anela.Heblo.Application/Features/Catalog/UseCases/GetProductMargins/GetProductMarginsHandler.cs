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

            var products = await GetProducts(cancellationToken);
            var filteredProducts = ApplyFilters(products, request);
            var totalCount = filteredProducts.Count();

            // Apply sorting directly on CatalogAggregate entities
            var sortedProducts = ApplySorting(filteredProducts, request.SortBy, request.SortDescending);

            // Apply pagination
            var pagedProducts = sortedProducts
                .Skip((request.PageNumber - 1) * request.PageSize)
                .Take(request.PageSize)
                .ToList();

            // Create DTOs using pre-calculated margin data
            var items = pagedProducts.Select(MapToMarginDto).ToList();

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

    private IEnumerable<CatalogAggregate> ApplyFilters(IEnumerable<CatalogAggregate> products, GetProductMarginsRequest request)
    {
        try
        {
            var filtered = products;

            if (!string.IsNullOrWhiteSpace(request.ProductCode))
            {
                filtered = filtered.Where(x => x.ProductCode != null && x.ProductCode.Contains(request.ProductCode, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(request.ProductName))
            {
                filtered = filtered.Where(x => x.ProductName != null && x.ProductName.Contains(request.ProductName, StringComparison.OrdinalIgnoreCase));
            }

            if (request.ProductType.HasValue)
            {
                filtered = filtered.Where(x => x.Type == request.ProductType.Value);
            }
            else
            {
                // Default filter: only Product and Goods
                filtered = filtered.Where(x => x.Type == ProductType.Product || x.Type == ProductType.Goods);
            }

            return filtered;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying filters to product query");
            throw; // Re-throw to be caught by the main handler
        }
    }

    private IEnumerable<CatalogAggregate> ApplySorting(IEnumerable<CatalogAggregate> products, string? sortBy, bool sortDescending)
    {
        if (string.IsNullOrWhiteSpace(sortBy))
        {
            // Default sorting by ProductCode
            return sortDescending
                ? products.OrderByDescending(x => x.ProductCode)
                : products.OrderBy(x => x.ProductCode);
        }

        return sortBy.ToLower() switch
        {
            "productcode" => sortDescending
                ? products.OrderByDescending(x => x.ProductCode)
                : products.OrderBy(x => x.ProductCode),
            "productname" => sortDescending
                ? products.OrderByDescending(x => x.ProductName)
                : products.OrderBy(x => x.ProductName),
            "pricewithoutvat" => sortDescending
                ? products.OrderByDescending(x => x.PriceWithoutVat ?? 0)
                : products.OrderBy(x => x.PriceWithoutVat ?? 0),
            "purchaseprice" => sortDescending
                ? products.OrderByDescending(x => x.ErpPrice?.PurchasePrice ?? 0)
                : products.OrderBy(x => x.ErpPrice?.PurchasePrice ?? 0),
            "manufacturedifficulty" => sortDescending
                ? products.OrderByDescending(x => x.ManufactureDifficulty ?? 0)
                : products.OrderBy(x => x.ManufactureDifficulty ?? 0),
            // M0-M3 margin levels - amounts (using pre-calculated data)
            "m0amount" => sortDescending
                ? products.OrderByDescending(x => x.Margins.Averages.M0.Amount)
                : products.OrderBy(x => x.Margins.Averages.M0.Amount),
            "m1amount" => sortDescending
                ? products.OrderByDescending(x => x.Margins.Averages.M1_A.Amount)
                : products.OrderBy(x => x.Margins.Averages.M1_A.Amount),
            "m2amount" => sortDescending
                ? products.OrderByDescending(x => x.Margins.Averages.M2.Amount)
                : products.OrderBy(x => x.Margins.Averages.M2.Amount),
            "m3amount" => sortDescending
                ? products.OrderByDescending(x => x.Margins.Averages.M3.Amount)
                : products.OrderBy(x => x.Margins.Averages.M3.Amount),
            // M0-M3 margin levels - percentages (using pre-calculated data)
            "m0percentage" => sortDescending
                ? products.OrderByDescending(x => x.Margins.Averages.M0.Percentage)
                : products.OrderBy(x => x.Margins.Averages.M0.Percentage),
            "m1percentage" => sortDescending
                ? products.OrderByDescending(x => x.Margins.Averages.M1_A.Percentage)
                : products.OrderBy(x => x.Margins.Averages.M1_A.Percentage),
            "m2percentage" => sortDescending
                ? products.OrderByDescending(x => x.Margins.Averages.M2.Percentage)
                : products.OrderBy(x => x.Margins.Averages.M2.Percentage),
            "m3percentage" => sortDescending
                ? products.OrderByDescending(x => x.Margins.Averages.M3.Percentage)
                : products.OrderBy(x => x.Margins.Averages.M3.Percentage),
            _ => sortDescending
                ? products.OrderByDescending(x => x.ProductCode)
                : products.OrderBy(x => x.ProductCode)
        };
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
                M1_A = new MarginLevelDto
                {
                    Percentage = marginHistory.Averages.M1_A.Percentage,
                    Amount = marginHistory.Averages.M1_A.Amount,
                    CostLevel = marginHistory.Averages.M1_A.CostLevel,
                    CostTotal = marginHistory.Averages.M1_A.CostTotal
                },
                M1_B = new MarginLevelDto
                {
                    Percentage = marginHistory.Averages.M1_B.Percentage,
                    Amount = marginHistory.Averages.M1_B.Amount,
                    CostLevel = marginHistory.Averages.M1_B.CostLevel,
                    CostTotal = marginHistory.Averages.M1_B.CostTotal
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
                    M1_A = new MarginLevelDto
                    {
                        Percentage = m.M1_A.Percentage,
                        Amount = m.M1_A.Amount,
                        CostLevel = m.M1_A.CostLevel,
                        CostTotal = m.M1_A.CostTotal
                    },
                    M1_B = m.M1_B != null ? new MarginLevelDto
                    {
                        Percentage = m.M1_B.Percentage,
                        Amount = m.M1_B.Amount,
                        CostLevel = m.M1_B.CostLevel,
                        CostTotal = m.M1_B.CostTotal
                    } : null,
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


}