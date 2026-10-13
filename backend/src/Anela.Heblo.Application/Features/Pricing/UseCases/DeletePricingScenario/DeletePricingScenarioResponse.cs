using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Pricing.UseCases.DeletePricingScenario;

public class DeletePricingScenarioResponse : BaseResponse
{
    public DeletePricingScenarioResponse() { }

    public DeletePricingScenarioResponse(ErrorCodes errorCode, Dictionary<string, string> parameters)
        : base(errorCode, parameters) { }
}
