using Anela.Heblo.Application.Features.Analytics;
using Anela.Heblo.Application.Features.Analytics.Contracts;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = AuthorizationConstants.Roles.FinanceReader)]
public class AnalyticsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AnalyticsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("product-margin-summary")]
    [ProducesResponseType(typeof(GetProductMarginSummaryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetProductMarginSummary([FromQuery] GetProductMarginSummaryRequest request)
    {
        var response = await _mediator.Send(request);
        return Ok(response);
    }
}