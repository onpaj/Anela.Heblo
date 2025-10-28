using Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchPlan;
using Anela.Heblo.Domain.Features.Manufacture;

namespace Anela.Heblo.Application.Features.Manufacture.Services;

public interface IBatchPlanningService
{
    Task<CalculateBatchPlanResponse> CalculateBatchPlan(CalculateBatchPlanRequest request, CancellationToken cancellationToken = default);
}

public class SinglePhaseBatchResult
{
    public ManufactureType ManufactureType { get; set; } = ManufactureType.SinglePhase;
    public List<MaterialRequirement> MaterialRequirements { get; set; } = new();
    public List<ProductionOutput> ProductOutputs { get; set; } = new();
    public double ScaleFactor { get; set; }
    public bool Success { get; set; } = true;
    public string? ErrorMessage { get; set; }

    public static SinglePhaseBatchResult Failed(string errorMessage)
    {
        return new SinglePhaseBatchResult
        {
            Success = false,
            ErrorMessage = errorMessage
        };
    }
}

public class MaterialRequirement
{
    public string MaterialCode { get; set; } = null!;
    public string MaterialName { get; set; } = null!;
    public double RequiredQuantity { get; set; }
    public string Unit { get; set; } = null!;
}

public class ProductionOutput
{
    public string ProductCode { get; set; } = null!;
    public string ProductName { get; set; } = null!;
    public double ExpectedQuantity { get; set; }
    public string Unit { get; set; } = null!;
}