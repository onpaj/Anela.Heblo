using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchPlan;

public class CalculateBatchPlanResponse : BaseResponse
{
    public SemiproductInfoDto Semiproduct { get; set; } = null!;
    public List<BatchPlanItemDto> ProductSizes { get; set; } = new();
    public BatchPlanSummaryDto Summary { get; set; } = null!;
    public double TargetDaysCoverage { get; set; }
    public double TotalVolumeUsed { get; set; }
    public double TotalVolumeAvailable { get; set; }

    public CalculateBatchPlanResponse() : base() { }

    public CalculateBatchPlanResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }
}

public class SemiproductInfoDto
{
    public string ProductCode { get; set; } = null!;
    public string ProductName { get; set; } = null!;
    public double AvailableStock { get; set; }

    public double MinimalManufactureQuantity { get; set; }
}

public class BatchPlanItemDto
{
    public string ProductCode { get; set; } = null!;
    public string ProductName { get; set; } = null!;
    public string ProductSize { get; set; } = null!;

    // Current State
    public double CurrentStock { get; set; }
    public double DailySalesRate { get; set; }
    public double CurrentDaysCoverage { get; set; }

    // Planned Production
    public int RecommendedUnitsToProduceHumanReadable { get; set; }
    public double WeightPerUnit { get; set; }           // From ManufactureTemplate.Ingredients
    public double TotalVolumeRequired { get; set; }

    // Future State
    public double FutureStock { get; set; }
    public double FutureDaysCoverage { get; set; }

    // Constraints & Control
    public bool IsFixed { get; set; }                     // Is this product size fixed by user
    public int? UserFixedQuantity { get; set; }           // User-specified fixed quantity
    public bool WasOptimized { get; set; }                // Was this size optimized by algorithm
    public string OptimizationNote { get; set; } = "";    // Explanation of how this was calculated

    public bool Enabled { get; set; } = true;
}

public class BatchPlanSummaryDto
{
    public int TotalProductSizes { get; set; }
    public double TotalVolumeUsed { get; set; }
    public double TotalVolumeAvailable { get; set; }
    public double VolumeUtilizationPercentage { get; set; }

    // Enhanced summary for different control modes
    public BatchPlanControlMode UsedControlMode { get; set; }
    public double EffectiveMmqMultiplier { get; set; }     // What multiplier was actually achieved
    public double ActualTotalWeight { get; set; }          // Total weight actually used
    public double AchievedAverageCoverage { get; set; }    // Average days coverage achieved
    public int FixedProductsCount { get; set; }            // How many products were fixed
    public int OptimizedProductsCount { get; set; }        // How many were optimized
}