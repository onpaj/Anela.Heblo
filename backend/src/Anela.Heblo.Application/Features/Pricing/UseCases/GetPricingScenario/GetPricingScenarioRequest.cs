using MediatR;

namespace Anela.Heblo.Application.Features.Pricing.UseCases.GetPricingScenario;

public class GetPricingScenarioRequest : IRequest<GetPricingScenarioResponse>
{
    public Guid Id { get; set; }
}
