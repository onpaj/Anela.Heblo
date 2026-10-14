using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Pricing;
using MediatR;

namespace Anela.Heblo.Application.Features.Pricing.UseCases.DeletePricingScenario;

public class DeletePricingScenarioHandler
    : IRequestHandler<DeletePricingScenarioRequest, DeletePricingScenarioResponse>
{
    private readonly IPricingScenarioRepository _repository;

    public DeletePricingScenarioHandler(IPricingScenarioRepository repository)
    {
        _repository = repository;
    }

    public async Task<DeletePricingScenarioResponse> Handle(
        DeletePricingScenarioRequest request, CancellationToken cancellationToken)
    {
        var scenario = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (scenario is null)
        {
            return new DeletePricingScenarioResponse(ErrorCodes.PricingScenarioNotFound, new Dictionary<string, string>
            {
                { "scenarioId", request.Id.ToString() }
            });
        }

        await _repository.DeleteAsync(request.Id, cancellationToken);

        return new DeletePricingScenarioResponse();
    }
}
