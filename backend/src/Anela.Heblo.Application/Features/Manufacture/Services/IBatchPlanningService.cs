using Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchPlan;

namespace Anela.Heblo.Application.Features.Manufacture.Services;

public interface IBatchPlanningService
{
    Task<CalculateBatchPlanResponse> CalculateBatchPlan(CalculateBatchPlanRequest request, CancellationToken cancellationToken = default);
}