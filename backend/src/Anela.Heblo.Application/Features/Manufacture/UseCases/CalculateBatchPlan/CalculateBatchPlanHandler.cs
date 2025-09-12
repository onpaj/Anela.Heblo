using Anela.Heblo.Application.Features.Manufacture.Services;
using MediatR;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.CalculateBatchPlan;

public class CalculateBatchPlanHandler : IRequestHandler<CalculateBatchPlanRequest, CalculateBatchPlanResponse>
{
    private readonly IBatchPlanningService _batchPlanningService;

    public CalculateBatchPlanHandler(IBatchPlanningService batchPlanningService)
    {
        _batchPlanningService = batchPlanningService;
    }

    public async Task<CalculateBatchPlanResponse> Handle(CalculateBatchPlanRequest request, CancellationToken cancellationToken)
    {
        return await _batchPlanningService.CalculateBatchPlan(request, cancellationToken);
    }
}