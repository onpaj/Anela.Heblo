using Anela.Heblo.Application.Features.FinancialOverview.Model;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = AuthorizationConstants.Roles.FinanceReader)]
public class FinancialOverviewController : ControllerBase
{
    private readonly IMediator _mediator;

    public FinancialOverviewController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    [ProducesResponseType(typeof(GetFinancialOverviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetFinancialOverview([FromQuery] int? months = 6, [FromQuery] bool includeStockData = true)
    {
        var request = new GetFinancialOverviewRequest
        {
            Months = months,
            IncludeStockData = includeStockData
        };
        var response = await _mediator.Send(request);

        return Ok(response);
    }
}