using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Domain.Features.Manufacture;
using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchPlan;

public class CalculateBatchPlanRequest : IRequest<CalculateBatchPlanResponse>
{
    [Required]
    public string SemiproductCode { get; set; } = null!;

    // Time period selection (same as Purchase Analysis)
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }

    // Sales calculation settings
    public double? SalesMultiplier { get; set; } = 1.0;

    // Control Mode - exactly one should be set
    public BatchPlanControlMode ControlMode { get; set; }
    public double? MmqMultiplier { get; set; }        // Mode 1: MMQ Multiplier (1.0 = 1x MMQ)
    public double? TotalWeightToUse { get; set; }     // Mode 2: Total semiproduct weight (ml/g)
    public double? TargetDaysCoverage { get; set; }   // Mode 3: Target days coverage for all sizes

    // Product size constraints
    public List<ProductSizeConstraint> ProductConstraints { get; set; } = new();
    
    // Manufacturing type - set internally by handler
    public ManufactureType? ManufactureType { get; set; }
}

public enum BatchPlanControlMode
{
    MmqMultiplier = 1,
    TotalWeight = 2,
    TargetDaysCoverage = 3
}

public class ProductSizeConstraint
{
    public string ProductCode { get; set; } = null!;
    public bool IsFixed { get; set; }                 // If true, quantity won't be optimized
    public int? FixedQuantity { get; set; }          // Required if IsFixed = true
}