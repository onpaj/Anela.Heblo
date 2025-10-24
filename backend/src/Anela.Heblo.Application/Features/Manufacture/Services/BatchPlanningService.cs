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

    public async Task<CalculateBatchPlanResponse> CalculateBatchPlan(CalculateBatchPlanRequest request, CancellationToken cancellationToken = default)
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

            var dailySalesRate = CalculateDailySalesRate(product, request.FromDate, request.ToDate, request.SalesMultiplier ?? 1.0);
            var currentDaysCoverage = dailySalesRate > 0 ? (double)product.Stock.Total / dailySalesRate : 0;

            var constraint = request.ProductConstraints.FirstOrDefault(c => c.ProductCode == template.ProductCode);
            var item = new BatchPlanItemDto
            {
                ProductCode = template.ProductCode,
                ProductName = template.ProductName,
                ProductSize = product.SizeCode ?? "",
                CurrentStock = (double)product.Stock.Total,
                DailySalesRate = dailySalesRate,
                CurrentDaysCoverage = currentDaysCoverage,
                WeightPerUnit = product.NetWeight ?? 0, // How much semiproduct this product consumes
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

    public async Task<SinglePhaseBatchResult> CalculateSinglePhaseBatchAsync(string productCode, double targetQuantity, CancellationToken cancellationToken = default)
    {
        try
        {
            // 1. Get product info
            var product = await _catalogRepository.GetByIdAsync(productCode, cancellationToken);
            if (product == null)
            {
                return SinglePhaseBatchResult.Failed($"Product with code '{productCode}' not found.");
            }

            // 2. Get direct ingredients for this product (bypassing semi-products)
            var directIngredients = await GetDirectIngredientsAsync(productCode, cancellationToken);
            if (directIngredients.Count == 0)
            {
                return SinglePhaseBatchResult.Failed($"No direct ingredients found for product '{productCode}'.");
            }

            // 3. Calculate scale factor based on minimal manufacture quantity
            var standardBatchSize = product.MinimalManufactureQuantity > 0 ? product.MinimalManufactureQuantity : 1.0;
            var scaleFactor = targetQuantity / standardBatchSize;

            // 4. Calculate material requirements
            var materialRequirements = directIngredients.Select(ingredient => new MaterialRequirement
            {
                MaterialCode = ingredient.MaterialCode,
                MaterialName = ingredient.MaterialName,
                RequiredQuantity = ingredient.Quantity * scaleFactor,
                Unit = ingredient.Unit
            }).ToList();

            // 5. Calculate expected output
            var productOutputs = new List<ProductionOutput>
            {
                new()
                {
                    ProductCode = productCode,
                    ProductName = product.ProductName,
                    ExpectedQuantity = targetQuantity,
                    Unit = "ks" // Default unit, could be enhanced to lookup from product properties
                }
            };

            return new SinglePhaseBatchResult
            {
                ManufactureType = ManufactureType.SinglePhase,
                MaterialRequirements = materialRequirements,
                ProductOutputs = productOutputs,
                ScaleFactor = scaleFactor,
                Success = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating single-phase batch for product {ProductCode}", productCode);
            return SinglePhaseBatchResult.Failed($"Error calculating batch: {ex.Message}");
        }
    }

    private async Task<List<DirectIngredient>> GetDirectIngredientsAsync(string productCode, CancellationToken cancellationToken)
    {
        // This is a simplified implementation. In a real system, this would:
        // 1. Query the recipe/BOM system to get direct materials for the product
        // 2. Look up material master data for names, units, etc.
        // 3. Handle complex recipe structures

        // For now, return a mock implementation
        // TODO: Implement actual recipe/ingredient lookup logic
        return new List<DirectIngredient>();
    }

    private class DirectIngredient
    {
        public string MaterialCode { get; set; } = null!;
        public string MaterialName { get; set; } = null!;
        public double Quantity { get; set; }
        public string Unit { get; set; } = null!;
    }
}