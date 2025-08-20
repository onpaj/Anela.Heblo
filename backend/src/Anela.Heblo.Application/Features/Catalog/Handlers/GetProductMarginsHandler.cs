using MediatR;
using Anela.Heblo.Application.Features.Catalog.Model;
using Anela.Heblo.Domain.Accounting.Ledger;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Manufacture;

namespace Anela.Heblo.Application.Features.Catalog;

public class GetProductMarginsHandler : IRequestHandler<GetProductMarginsRequest, GetProductMarginsResponse>
{
    private readonly ICatalogRepository _catalogRepository;
    private readonly ILedgerService _ledgerService;
    private readonly TimeProvider _timeProvider;

    public GetProductMarginsHandler(
        ICatalogRepository catalogRepository,
        ILedgerService ledgerService,
        TimeProvider timeProvider
        )
    {
        _catalogRepository = catalogRepository;
        _ledgerService = ledgerService;
        _timeProvider = timeProvider;
    }

    public async Task<GetProductMarginsResponse> Handle(GetProductMarginsRequest request, CancellationToken cancellationToken)
    {
        var allItems = await _catalogRepository.GetAllAsync(cancellationToken);
        var query = allItems.AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.ProductCode))
        {
            query = query.Where(x => x.ProductCode.Contains(request.ProductCode, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(request.ProductName))
        {
            query = query.Where(x => x.ProductName.Contains(request.ProductName, StringComparison.OrdinalIgnoreCase));
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

        // Convert to list first to enable in-memory operations for complex sorting
        var filteredItems = query.ToList();
        var totalCount = filteredItems.Count;

        // Create DTOs with calculated values
        var itemsWithCalculatedValues = filteredItems
            .Select(x => new ProductMarginDto
            {
                ProductCode = x.ProductCode,
                ProductName = x.ProductName,
                PriceWithoutVat = x.EshopPrice?.PriceWithoutVat,
                PurchasePrice = x.ErpPrice?.PurchasePrice,
                ManufactureDifficulty = x.ManufactureDifficulty,
                AverageMaterialCost = CalculateAverageMaterialCostFromHistory(x.ManufactureCostHistory),
                AverageHandlingCost = CalculateAverageHandlingCostFromHistory(x.ManufactureCostHistory),
                MarginPercentage = x.MarginPercentage,
                MarginAmount = x.MarginAmount
            })
            .ToList();

        // Apply sorting in memory
        itemsWithCalculatedValues = ApplySortingInMemory(itemsWithCalculatedValues, request.SortBy, request.SortDescending);

        // Apply pagination
        var items = itemsWithCalculatedValues
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        return new GetProductMarginsResponse
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize
        };
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