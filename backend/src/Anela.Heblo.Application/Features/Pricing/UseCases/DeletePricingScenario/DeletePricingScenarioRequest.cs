using MediatR;

namespace Anela.Heblo.Application.Features.Pricing.UseCases.DeletePricingScenario;

public class DeletePricingScenarioRequest : IRequest<DeletePricingScenarioResponse>
{
    public Guid Id { get; set; }
}
