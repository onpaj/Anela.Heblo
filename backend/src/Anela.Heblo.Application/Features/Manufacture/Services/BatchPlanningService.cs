using Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchPlan;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Manufacture;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Manufacture.Services;

public class BatchPlanningService : IBatchPlanningService
{
    private readonly ICatalogRepository _catalogRepository;
    private readonly IManufactureRepository _manufactureRepository;
    private readonly IBatchDistributionCalculator _batchDistributionCalculator;
    private readonly ILogger<BatchPlanningService> _logger;

    public BatchPlanningService(
        ICatalogRepository catalogRepository,
        IManufactureRepository manufactureRepository,
        IBatchDistributionCalculator batchDistributionCalculator,
        ILogger<BatchPlanningService> logger)
    {
        _catalogRepository = catalogRepository;
        _manufactureRepository = manufactureRepository;
        _batchDistributionCalculator = batchDistributionCalculator;
        _logger = logger;
    }

    public async Task<CalculateBatchPlanResponse> CalculateBatchPlan(CalculateBatchPlanRequest request, CancellationToken cancellationToken)
    {
        if (request.ManufactureType == ManufactureType.SinglePhase)
        {
            return await CalculateSinglePhaseBatchPlanInternal(request, cancellationToken);
        }
        else
        {
            return await CalculateMultiPhaseBatchPlanInternal(request, cancellationToken);
        }
    }

    private double CalculateDailySalesRate(CatalogAggregate product, DateTime? fromDate, DateTime? toDate, double salesMultiplier = 1.0)
    {
        // Set default time period if not provided
        var endDate = toDate ?? DateTime.Now;
        var startDate = fromDate ?? endDate.AddDays(-30); // Default 30 days

        var totalSales = product.GetTotalSold(startDate, endDate);
        var days = (endDate - startDate).TotalDays;

        var baseDailySalesRate = days > 0 ? totalSales / days : 0;

        // Apply sales multiplier to affect future production planning
        return Math.Round(baseDailySalesRate * salesMultiplier, 2);
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
            fixedProduct.TotalVolumeRequired = quantity * fixedProduct.WeightPerUnit;
            fixedProduct.FutureStock = fixedProduct.CurrentStock + quantity;
            fixedProduct.FutureDaysCoverage = fixedProduct.DailySalesRate > 0
                ? fixedProduct.FutureStock / fixedProduct.DailySalesRate
                : double.MaxValue;
            fixedProduct.OptimizationNote = "Fixed by user constraint";

            volumeUsedByFixed += fixedProduct.TotalVolumeRequired;
        }

        // Remaining volume for flexible products
        var remainingVolume = availableVolume - volumeUsedByFixed;

        // Handle case where fixed products exceed available volume
        if (remainingVolume < 0)
        {
            // Calculate summary even with invalid state
            var invalidSummary = CalculateSummary(batchPlanItems, request, availableVolume);

            return new CalculateBatchPlanResponse(ErrorCodes.FixedProductsExceedAvailableVolume, new Dictionary<string, string>
            {
                { "volumeUsedByFixed", volumeUsedByFixed.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) },
                { "availableVolume", availableVolume.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) },
                { "deficit", Math.Abs(remainingVolume).ToString("F2", System.Globalization.CultureInfo.InvariantCulture) }
            })
            {
                ManufactureType = ManufactureType.MultiPhase,
                Semiproduct = new SemiproductInfoDto
                {
                    ProductCode = semiproduct.ProductCode,
                    ProductName = semiproduct.ProductName,
                    AvailableStock = availableVolume,
                    MinimalManufactureQuantity = semiproduct.MinimalManufactureQuantity,
                },
                ProductSizes = batchPlanItems,
                Summary = invalidSummary,
                TargetDaysCoverage = request.TargetDaysCoverage ?? CalculateAverageCoverage(batchPlanItems),
                TotalVolumeUsed = batchPlanItems.Sum(x => x.TotalVolumeRequired),
                TotalVolumeAvailable = availableVolume
            };
        }

        // Convert flexible products to ProductBatch format for BatchDistributionCalculator
        if (flexibleProducts.Count > 0)
        {
            var batch = CreateProductBatch(flexibleProducts, semiproduct, availableVolume, remainingVolume);

            // Use BatchDistributionCalculator for optimization
            _batchDistributionCalculator.OptimizeBatch(batch, minimizeResidue: true);

            // Convert results back to BatchPlanItemDto
            ApplyOptimizationResults(flexibleProducts, batch);
        }

        // Calculate summary
        var summary = CalculateSummary(batchPlanItems, request, availableVolume);

        return new CalculateBatchPlanResponse
        {
            ManufactureType = ManufactureType.MultiPhase,
            Semiproduct = new SemiproductInfoDto
            {
                ProductCode = semiproduct.ProductCode,
                ProductName = semiproduct.ProductName,
                AvailableStock = availableVolume,
                MinimalManufactureQuantity = semiproduct.MinimalManufactureQuantity,
            },
            ProductSizes = batchPlanItems,
            Summary = summary,
            TargetDaysCoverage = request.TargetDaysCoverage ?? CalculateAverageCoverage(batchPlanItems),
            TotalVolumeUsed = batchPlanItems.Sum(x => x.TotalVolumeRequired),
            TotalVolumeAvailable = availableVolume
        };
    }

    private ProductBatch CreateProductBatch(List<BatchPlanItemDto> flexibleProducts, CatalogAggregate semiproduct, double availableVolume, double remainingVolume)
    {
        var variants = flexibleProducts.Select(product => new ProductVariant
        {
            ProductCode = product.ProductCode,
            ProductName = product.ProductName,
            Weight = product.WeightPerUnit, // Volume per unit is the weight in the batch context
            DailySales = product.DailySalesRate,
            CurrentStock = product.CurrentStock,
            SuggestedAmount = 0 // Will be set by optimizer
        }).ToList();

        return new ProductBatch
        {
            ProductCode = semiproduct.ProductCode, // Generic batch identifier
            ProductName = semiproduct.ProductName,
            TotalWeight = remainingVolume,
            BatchSize = remainingVolume,
            BatchCount = 1,
            Variants = variants
        };
    }

    private void ApplyOptimizationResults(List<BatchPlanItemDto> flexibleProducts, ProductBatch batch)
    {
        foreach (var product in flexibleProducts)
        {
            var variant = batch.Variants.FirstOrDefault(v => v.ProductCode == product.ProductCode);
            if (variant != null)
            {
                product.RecommendedUnitsToProduceHumanReadable = (int)variant.SuggestedAmount;
                product.TotalVolumeRequired = variant.SuggestedAmount * product.WeightPerUnit;
                product.FutureStock = product.CurrentStock + variant.SuggestedAmount;
                product.FutureDaysCoverage = product.DailySalesRate > 0
                    ? product.FutureStock / product.DailySalesRate
                    : double.MaxValue;
                product.WasOptimized = true;
                product.OptimizationNote = $"Optimized by BatchDistributionCalculator (Coverage: {variant.UpstockTotal:F1} days)";
            }
        }
    }



    private BatchPlanSummaryDto CalculateSummary(List<BatchPlanItemDto> items, CalculateBatchPlanRequest request, double availableVolume)
    {
        var totalVolumeUsed = items.Sum(x => x.TotalVolumeRequired);
        var fixedCount = items.Count(x => x.IsFixed);
        var optimizedCount = items.Count(x => !x.IsFixed);

        // ActualTotalWeight should reflect the original user input for TotalWeight mode, not the calculated volume used
        var actualTotalWeight = request.ControlMode == BatchPlanControlMode.TotalWeight
            ? request.TotalWeightToUse ?? totalVolumeUsed
            : totalVolumeUsed;

        return new BatchPlanSummaryDto
        {
            TotalProductSizes = items.Count,
            TotalVolumeUsed = totalVolumeUsed,
            TotalVolumeAvailable = availableVolume,
            VolumeUtilizationPercentage = availableVolume > 0 ? (totalVolumeUsed / availableVolume) * 100 : 0,
            UsedControlMode = request.ControlMode,
            EffectiveMmqMultiplier = request.MmqMultiplier ?? 1.0,
            ActualTotalWeight = actualTotalWeight,
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

    private BatchPlanItemDto CreateBatchPlanItem(
        CatalogAggregate product, 
        CalculateBatchPlanRequest request, 
        string? productName = null)
    {
        var dailySalesRate = CalculateDailySalesRate(product, request.FromDate, request.ToDate, request.SalesMultiplier ?? 1.0);
        var currentDaysCoverage = dailySalesRate > 0 ? (double)product.Stock.Total / dailySalesRate : 0;
        var constraint = request.ProductConstraints.FirstOrDefault(c => c.ProductCode == product.ProductCode);

        return new BatchPlanItemDto
        {
            ProductCode = product.ProductCode,
            ProductName = productName ?? product.ProductName,
            ProductSize = product.SizeCode ?? "",
            CurrentStock = (double)product.Stock.Total,
            DailySalesRate = dailySalesRate,
            CurrentDaysCoverage = currentDaysCoverage,
            WeightPerUnit = product.NetWeight ?? 0,
            IsFixed = constraint?.IsFixed ?? false,
            UserFixedQuantity = constraint?.FixedQuantity,
            WasOptimized = false,
            OptimizationNote = ""
        };
    }

    private (double targetProduction, double availableVolume) CalculateTargetProductionAndVolume(
        CatalogAggregate product, 
        CalculateBatchPlanRequest request, 
        double dailySalesRate)
    {
        double targetProduction = 0;
        double availableVolume = 0;
        
        switch (request.ControlMode)
        {
            case BatchPlanControlMode.MmqMultiplier:
                availableVolume = (request.MmqMultiplier ?? 1.0) * product.MinimalManufactureQuantity;
                targetProduction = availableVolume / (product.NetWeight ?? 1);
                break;
            case BatchPlanControlMode.TotalWeight:
                availableVolume = request.TotalWeightToUse ?? 0;
                targetProduction = availableVolume / (product.NetWeight ?? 1);
                break;
            case BatchPlanControlMode.TargetDaysCoverage:
                targetProduction = (request.TargetDaysCoverage ?? 30) * dailySalesRate;
                availableVolume = targetProduction * (product.NetWeight ?? 1);
                break;
        }

        return (targetProduction, availableVolume);
    }

    private (double targetProductionUnits, double totalWeight) CalculateSinglePhaseTargetProduction(
        CatalogAggregate product, 
        CalculateBatchPlanRequest request, 
        double dailySalesRate)
    {
        double targetProductionUnits = 0;
        double totalWeight = 0;
        
        switch (request.ControlMode)
        {
            case BatchPlanControlMode.MmqMultiplier:
                // V single-phase je MMQ už v kusech produktu
                targetProductionUnits = (request.MmqMultiplier ?? 1.0) * product.MinimalManufactureQuantity;
                totalWeight = targetProductionUnits * (product.NetWeight ?? 1);
                break;
            case BatchPlanControlMode.TotalWeight:
                // Zadáváme celkovou hmotnost, počítáme kusy
                totalWeight = request.TotalWeightToUse ?? 0;
                targetProductionUnits = totalWeight / (product.NetWeight ?? 1);
                break;
            case BatchPlanControlMode.TargetDaysCoverage:
                // Počítáme kusy pro pokrytí dnů prodeje
                targetProductionUnits = (request.TargetDaysCoverage ?? 30) * dailySalesRate;
                totalWeight = targetProductionUnits * (product.NetWeight ?? 1);
                break;
        }

        return (targetProductionUnits, totalWeight);
    }

    

    private async Task<CalculateBatchPlanResponse> CalculateSinglePhaseBatchPlanInternal(CalculateBatchPlanRequest request, CancellationToken cancellationToken)
    {
        // For single-phase manufacturing, the request.SemiproductCode is actually the product code
        var product = await _catalogRepository.GetByIdAsync(request.SemiproductCode, cancellationToken);
        if (product == null)
        {
            throw new ArgumentException($"Product with code '{request.SemiproductCode}' not found.");
        }

        // Create batch plan item using common helper
        var batchPlanItem = CreateBatchPlanItem(product, request);

        // Calculate target production in units (not weight) for single-phase
        var (targetProductionUnits, totalWeight) = CalculateSinglePhaseTargetProduction(product, request, batchPlanItem.DailySalesRate);

        // Set production values - targetProductionUnits is already in pieces/units
        batchPlanItem.RecommendedUnitsToProduceHumanReadable = (int)Math.Round(targetProductionUnits);
        batchPlanItem.TotalVolumeRequired = totalWeight; // Total weight of produced units
        batchPlanItem.FutureStock = batchPlanItem.CurrentStock + targetProductionUnits; // Stock in units
        batchPlanItem.FutureDaysCoverage = batchPlanItem.DailySalesRate > 0 ? batchPlanItem.FutureStock / batchPlanItem.DailySalesRate : 0;
        batchPlanItem.OptimizationNote = "Single-phase manufacturing";

        var batchPlanItems = new List<BatchPlanItemDto> { batchPlanItem };
        var summary = CalculateSummary(batchPlanItems, request, totalWeight);

        return new CalculateBatchPlanResponse
        {
            Success = true,
            ManufactureType = ManufactureType.SinglePhase,
            Semiproduct = new SemiproductInfoDto
            {
                ProductCode = product.ProductCode,
                ProductName = product.ProductName,
                AvailableStock = (double)product.Stock.Total, // Stock in units
                MinimalManufactureQuantity = product.MinimalManufactureQuantity // MMQ in units
            },
            ProductSizes = batchPlanItems,
            Summary = summary,
            TargetDaysCoverage = request.TargetDaysCoverage ?? 30,
            TotalVolumeUsed = batchPlanItem.TotalVolumeRequired,
            TotalVolumeAvailable = totalWeight
        };
    }

    private async Task<CalculateBatchPlanResponse> CalculateMultiPhaseBatchPlanInternal(CalculateBatchPlanRequest request, CancellationToken cancellationToken)
    {
        // 1. Get semiproduct info
        var semiproduct = await _catalogRepository.GetByIdAsync(request.SemiproductCode, cancellationToken);
        if (semiproduct == null)
        {
            throw new ArgumentException($"Semiproduct with code '{request.SemiproductCode}' not found.");
        }

        var availableVolume = request.TotalWeightToUse ?? (request.MmqMultiplier ?? 1.0) * semiproduct.MinimalManufactureQuantity;

        // 2. Find all products that use this semiproduct
        var productTemplates = await _manufactureRepository.FindByIngredientAsync(request.SemiproductCode, cancellationToken);
        if (productTemplates.Count == 0)
        {
            throw new ArgumentException($"No products found that use semiproduct '{request.SemiproductCode}'.");
        }

        // 3. For each product, get catalog data and create batch plan items
        var batchPlanItems = new List<BatchPlanItemDto>();
        foreach (var template in productTemplates)
        {
            var product = await _catalogRepository.GetByIdAsync(template.ProductCode, cancellationToken);
            if (product == null)
            {
                _logger.LogWarning("Product {ProductCode} found in template but not in catalog", template.ProductCode);
                continue;
            }

            var item = CreateBatchPlanItem(product, request, template.ProductName);
            batchPlanItems.Add(item);
        }

        // 4. Apply optimization algorithm based on control mode
        var response = ApplyOptimization(batchPlanItems, request, availableVolume, semiproduct);

        return response;
    }

}