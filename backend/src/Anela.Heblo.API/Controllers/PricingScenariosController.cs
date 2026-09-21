using Anela.Heblo.Application.Features.Pricing.UseCases.DeletePricingScenario;
using Anela.Heblo.Application.Features.Pricing.UseCases.GetPricingScenario;
using Anela.Heblo.Application.Features.Pricing.UseCases.GetPricingScenarios;
using Anela.Heblo.Application.Features.Pricing.UseCases.SavePricingScenario;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[FeatureAuthorize(Feature.Finance_PriceAnalysis)]
[ApiController]
[Route("api/pricing-scenarios")]
public class PricingScenariosController : BaseApiController
{
    private readonly IMediator _mediator;

    public PricingScenariosController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<GetPricingScenariosResponse>> GetScenarios()
    {
        var response = await _mediator.Send(new GetPricingScenariosRequest());
        return HandleResponse(response);
    }

    [HttpPost]
    public async Task<ActionResult<SavePricingScenarioResponse>> CreateScenario(
        [FromBody] SavePricingScenarioRequest request)
    {
        var response = await _mediator.Send(request);
        return HandleResponse(response);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GetPricingScenarioResponse>> GetScenario(Guid id)
    {
        var response = await _mediator.Send(new GetPricingScenarioRequest { Id = id });
        return HandleResponse(response);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SavePricingScenarioResponse>> UpdateScenario(
        Guid id, [FromBody] SavePricingScenarioRequest request)
    {
        request.Id = id;
        var response = await _mediator.Send(request);
        return HandleResponse(response);
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<DeletePricingScenarioResponse>> DeleteScenario(Guid id)
    {
        var response = await _mediator.Send(new DeletePricingScenarioRequest { Id = id });
        return HandleResponse(response);
    }
}
