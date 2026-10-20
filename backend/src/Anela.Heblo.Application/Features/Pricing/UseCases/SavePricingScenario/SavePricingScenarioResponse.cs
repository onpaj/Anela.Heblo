using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Pricing.UseCases.SavePricingScenario;

public class SavePricingScenarioResponse : BaseResponse
{
    public Guid Id { get; set; }

    public SavePricingScenarioResponse() { }

    public SavePricingScenarioResponse(ErrorCodes errorCode, Dictionary<string, string> parameters)
        : base(errorCode, parameters) { }
}
