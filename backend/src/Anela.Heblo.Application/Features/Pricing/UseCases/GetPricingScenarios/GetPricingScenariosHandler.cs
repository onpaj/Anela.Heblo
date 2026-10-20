using Anela.Heblo.Application.Features.Pricing.Contracts;
using Anela.Heblo.Domain.Features.Pricing;
using MediatR;

namespace Anela.Heblo.Application.Features.Pricing.UseCases.GetPricingScenarios;

public class GetPricingScenariosHandler
    : IRequestHandler<GetPricingScenariosRequest, GetPricingScenariosResponse>
{
    private readonly IPricingScenarioRepository _repository;

    public GetPricingScenariosHandler(IPricingScenarioRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetPricingScenariosResponse> Handle(
        GetPricingScenariosRequest request, CancellationToken cancellationToken)
    {
        var scenarios = await _repository.GetAllAsync(cancellationToken);

        return new GetPricingScenariosResponse
        {
            Scenarios = scenarios.Select(ToSummary).ToList()
        };
    }

    private static PricingScenarioSummaryDto ToSummary(PricingScenario scenario) => new()
    {
        Id = scenario.Id,
        Name = scenario.Name,
        Description = scenario.Description,
        CreatedBy = scenario.CreatedBy,
        CreatedAt = scenario.CreatedAt,
        ModifiedAt = scenario.ModifiedAt,
        EditedProductCount = scenario.Items.Count
    };
}
