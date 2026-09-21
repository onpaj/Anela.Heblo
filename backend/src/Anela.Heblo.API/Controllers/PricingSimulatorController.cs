using Anela.Heblo.Application.Features.Pricing.UseCases.GetPricingBaseline;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[FeatureAuthorize(Feature.Finance_PriceAnalysis)]
[ApiController]
[Route("api/pricing-simulator")]
public class PricingSimulatorController : BaseApiController
{
    private readonly IMediator _mediator;

    public PricingSimulatorController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("baseline")]
    public async Task<ActionResult<GetPricingBaselineResponse>> GetBaseline(
        [FromQuery] GetPricingBaselineRequest request)
    {
        var response = await _mediator.Send(request);
        return HandleResponse(response);
    }
}
