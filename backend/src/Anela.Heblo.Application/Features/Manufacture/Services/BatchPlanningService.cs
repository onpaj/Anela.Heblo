using Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchPlan;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Manufacture;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Manufacture.Services;

public class BatchPlanningService : IBatchPlanningService
{
    private readonly ICatalogRepository _catalogRepository;
    private readonly IManufactureRepository _manufactureRepository;
    private readonly ILogger<BatchPlanningService> _logger;

    public BatchPlanningService(
        ICatalogRepository catalogRepository,
        IManufactureRepository manufactureRepository,
        ILogger<BatchPlanningService> logger)
    {
        _catalogRepository = catalogRepository;
        _manufactureRepository = manufactureRepository;
        _logger = logger;
    }

    public async Task<CalculateBatchPlanResponse> CalculateBatchPlan(CalculateBatchPlanRequest request, CancellationToken cancellationToken = default)
    {
        // 1. Get semiproduct info
        var semiproduct = await _catalogRepository.GetByIdAsync(request.SemiproductCode, cancellationToken);
        if (semiproduct == null)
        {
            throw new ArgumentException($"Semiproduct with code '{request.SemiproductCode}' not found.");
        }

        var availableVolume = (double)semiproduct.Stock.Erp;

        // 2. Find all products that use this semiproduct
        var productTemplates = await _manufactureRepository.FindByIngredientAsync(request.SemiproductCode, cancellationToken);
        if (productTemplates.Count == 0)
        {
            throw new ArgumentException($"No products found that use semiproduct '{request.SemiproductCode}'.");
        }

        // 3. For each product, get catalog data (sales, stock, MMQ)
        var batchPlanItems = new List<BatchPlanItemDto>();
        foreach (var template in productTemplates)
        {
            var product = await _catalogRepository.GetByIdAsync(template.ProductCode, cancellationToken);
            if (product == null)
            {
                _logger.LogWarning("Product {ProductCode} found in template but not in catalog", template.ProductCode);
                continue;
            }

            var ingredient = template.Ingredients.FirstOrDefault(i => i.ProductCode == request.SemiproductCode);
            if (ingredient == null)
            {
                _logger.LogWarning("Ingredient {SemiproductCode} not found in template for product {ProductCode}", request.SemiproductCode, template.ProductCode);
                continue;
            }

            var dailySalesRate = CalculateDailySalesRate(product, request.FromDate, request.ToDate);
            var currentDaysCoverage = dailySalesRate > 0 ? (double)product.Stock.Erp / dailySalesRate : double.MaxValue;

            var constraint = request.ProductConstraints.FirstOrDefault(c => c.ProductCode == template.ProductCode);
            var item = new BatchPlanItemDto
            {
                ProductCode = template.ProductCode,
                ProductName = template.ProductName,
                ProductSize = product.SizeCode ?? "",
                CurrentStock = (double)product.Stock.Erp,
                DailySalesRate = dailySalesRate,
                CurrentDaysCoverage = currentDaysCoverage == double.MaxValue ? 0 : currentDaysCoverage,
                VolumePerUnit = ingredient.Amount, // How much semiproduct this product consumes
                MinimalManufactureQuantity = product.MinimalManufactureQuantity,
                IsFixed = constraint?.IsFixed ?? false,
                UserFixedQuantity = constraint?.FixedQuantity,
                WasOptimized = false,
                OptimizationNote = ""
            };

            batchPlanItems.Add(item);
        }

        // 4. Apply optimization algorithm based on control mode
        var response = ApplyOptimization(batchPlanItems, request, availableVolume, semiproduct);

        return response;
    }

    private double CalculateDailySalesRate(CatalogAggregate product, DateTime? fromDate, DateTime? toDate)
    {
        // Set default time period if not provided
        var endDate = toDate ?? DateTime.Now;
        var startDate = fromDate ?? endDate.AddDays(-30); // Default 30 days

        var totalSales = product.GetTotalSold(startDate, endDate);
        var days = (endDate - startDate).TotalDays;

        return days > 0 ? totalSales / days : 0;
    }

    private CalculateBatchPlanResponse ApplyOptimization(
        List<BatchPlanItemDto> batchPlanItems,
        CalculateBatchPlanRequest request,
        double availableVolume,
        CatalogAggregate semiproduct)
    {
        // First, handle fixed products
        var fixedProducts = batchPlanItems.Where(x => x.IsFixed).ToList();
        var flexibleProducts = batchPlanItems.Where(x => !x.IsFixed).ToList();

        // Calculate volume used by fixed products
        double volumeUsedByFixed = 0;
        foreach (var fixedProduct in fixedProducts)
        {
            var quantity = fixedProduct.UserFixedQuantity ?? 0;
            fixedProduct.RecommendedUnitsToProduceHumanReadable = quantity;
            fixedProduct.TotalVolumeRequired = quantity * fixedProduct.VolumePerUnit;
            fixedProduct.FutureStock = fixedProduct.CurrentStock + quantity;
            fixedProduct.FutureDaysCoverage = fixedProduct.DailySalesRate > 0 
                ? fixedProduct.FutureStock / fixedProduct.DailySalesRate 
                : double.MaxValue;
            fixedProduct.OptimizationNote = "Fixed by user constraint";
            
            volumeUsedByFixed += fixedProduct.TotalVolumeRequired;
        }

        // Remaining volume for flexible products
        var remainingVolume = availableVolume - volumeUsedByFixed;

        if (remainingVolume < 0)
        {
            throw new InvalidOperationException("Fixed products require more volume than available semiproduct.");
        }

        // Apply control mode specific optimization
        switch (request.ControlMode)
        {
            case BatchPlanControlMode.MmqMultiplier:
                OptimizeMmqMultiplier(flexibleProducts, remainingVolume, request.MmqMultiplier ?? 1.0);
                break;
            case BatchPlanControlMode.TotalWeight:
                OptimizeTotalWeight(flexibleProducts, request.TotalWeightToUse ?? availableVolume, volumeUsedByFixed);
                break;
            case BatchPlanControlMode.TargetDaysCoverage:
                OptimizeTargetCoverage(flexibleProducts, request.TargetDaysCoverage ?? 30.0, remainingVolume);
                break;
        }

        // Calculate summary
        var summary = CalculateSummary(batchPlanItems, request, availableVolume);

        return new CalculateBatchPlanResponse
        {
            Semiproduct = new SemiproductInfoDto
            {
                ProductCode = semiproduct.ProductCode,
                ProductName = semiproduct.ProductName,
                AvailableStock = availableVolume
            },
            ProductSizes = batchPlanItems,
            Summary = summary,
            TargetDaysCoverage = request.TargetDaysCoverage ?? CalculateAverageCoverage(batchPlanItems),
            TotalVolumeUsed = batchPlanItems.Sum(x => x.TotalVolumeRequired),
            TotalVolumeAvailable = availableVolume
        };
    }

    private void OptimizeMmqMultiplier(List<BatchPlanItemDto> flexibleProducts, double remainingVolume, double multiplier)
    {
        foreach (var product in flexibleProducts)
        {
            var targetQuantity = (int)(product.MinimalManufactureQuantity * multiplier);
            var volumeRequired = targetQuantity * product.VolumePerUnit;

            // Scale down if not enough volume
            if (volumeRequired > remainingVolume)
            {
                targetQuantity = (int)(remainingVolume / product.VolumePerUnit);
                volumeRequired = targetQuantity * product.VolumePerUnit;
            }

            product.RecommendedUnitsToProduceHumanReadable = targetQuantity;
            product.TotalVolumeRequired = volumeRequired;
            product.FutureStock = product.CurrentStock + targetQuantity;
            product.FutureDaysCoverage = product.DailySalesRate > 0 
                ? product.FutureStock / product.DailySalesRate 
                : double.MaxValue;
            product.WasOptimized = true;
            product.OptimizationNote = $"Optimized using MMQ × {multiplier:F1}";

            remainingVolume -= volumeRequired;
        }
    }

    private void OptimizeTotalWeight(List<BatchPlanItemDto> flexibleProducts, double totalWeight, double volumeUsedByFixed)
    {
        var availableForFlexible = totalWeight - volumeUsedByFixed;
        
        if (availableForFlexible <= 0)
        {
            foreach (var product in flexibleProducts)
            {
                product.RecommendedUnitsToProduceHumanReadable = 0;
                product.TotalVolumeRequired = 0;
                product.FutureStock = product.CurrentStock;
                product.FutureDaysCoverage = product.CurrentDaysCoverage;
                product.WasOptimized = true;
                product.OptimizationNote = "No volume remaining after fixed products";
            }
            return;
        }

        // Simple proportional distribution based on current coverage need
        var totalCoverageNeed = flexibleProducts.Sum(p => p.DailySalesRate > 0 ? 1.0 / p.CurrentDaysCoverage : 0);
        
        foreach (var product in flexibleProducts)
        {
            if (totalCoverageNeed > 0 && product.DailySalesRate > 0)
            {
                var proportion = (1.0 / product.CurrentDaysCoverage) / totalCoverageNeed;
                var volumeForProduct = availableForFlexible * proportion;
                var quantity = (int)(volumeForProduct / product.VolumePerUnit);
                
                product.RecommendedUnitsToProduceHumanReadable = quantity;
                product.TotalVolumeRequired = quantity * product.VolumePerUnit;
            }
            else
            {
                product.RecommendedUnitsToProduceHumanReadable = 0;
                product.TotalVolumeRequired = 0;
            }

            product.FutureStock = product.CurrentStock + product.RecommendedUnitsToProduceHumanReadable;
            product.FutureDaysCoverage = product.DailySalesRate > 0 
                ? product.FutureStock / product.DailySalesRate 
                : double.MaxValue;
            product.WasOptimized = true;
            product.OptimizationNote = $"Optimized for total weight {totalWeight:F0}ml";
        }
    }

    private void OptimizeTargetCoverage(List<BatchPlanItemDto> flexibleProducts, double targetDays, double remainingVolume)
    {
        foreach (var product in flexibleProducts)
        {
            if (product.DailySalesRate <= 0)
            {
                product.RecommendedUnitsToProduceHumanReadable = 0;
                product.TotalVolumeRequired = 0;
                product.FutureStock = product.CurrentStock;
                product.FutureDaysCoverage = product.CurrentDaysCoverage;
                product.WasOptimized = true;
                product.OptimizationNote = "No sales data available";
                continue;
            }

            var targetStock = product.DailySalesRate * targetDays;
            var additionalUnitsNeeded = Math.Max(0, (int)(targetStock - product.CurrentStock));
            var volumeRequired = additionalUnitsNeeded * product.VolumePerUnit;

            // Scale down if not enough volume
            if (volumeRequired > remainingVolume)
            {
                additionalUnitsNeeded = (int)(remainingVolume / product.VolumePerUnit);
                volumeRequired = additionalUnitsNeeded * product.VolumePerUnit;
            }

            product.RecommendedUnitsToProduceHumanReadable = additionalUnitsNeeded;
            product.TotalVolumeRequired = volumeRequired;
            product.FutureStock = product.CurrentStock + additionalUnitsNeeded;
            product.FutureDaysCoverage = product.DailySalesRate > 0 
                ? product.FutureStock / product.DailySalesRate 
                : double.MaxValue;
            product.WasOptimized = true;
            product.OptimizationNote = $"Optimized for {targetDays} days coverage";

            remainingVolume -= volumeRequired;
        }
    }

    private BatchPlanSummaryDto CalculateSummary(List<BatchPlanItemDto> items, CalculateBatchPlanRequest request, double availableVolume)
    {
        var totalVolumeUsed = items.Sum(x => x.TotalVolumeRequired);
        var fixedCount = items.Count(x => x.IsFixed);
        var optimizedCount = items.Count(x => !x.IsFixed);

        return new BatchPlanSummaryDto
        {
            TotalProductSizes = items.Count,
            TotalVolumeUsed = totalVolumeUsed,
            TotalVolumeAvailable = availableVolume,
            VolumeUtilizationPercentage = availableVolume > 0 ? (totalVolumeUsed / availableVolume) * 100 : 0,
            UsedControlMode = request.ControlMode,
            EffectiveMmqMultiplier = request.MmqMultiplier ?? 1.0,
            ActualTotalWeight = totalVolumeUsed,
            AchievedAverageCoverage = CalculateAverageCoverage(items),
            FixedProductsCount = fixedCount,
            OptimizedProductsCount = optimizedCount
        };
    }

    private double CalculateAverageCoverage(List<BatchPlanItemDto> items)
    {
        var validCoverages = items.Where(x => x.FutureDaysCoverage < double.MaxValue).Select(x => x.FutureDaysCoverage).ToList();
        return validCoverages.Count > 0 ? validCoverages.Average() : 0;
    }
}