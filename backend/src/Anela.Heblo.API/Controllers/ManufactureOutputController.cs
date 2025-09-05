using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureOutput;
using Anela.Heblo.Application.Shared;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[Authorize]
[ApiController]
[Route("api/manufacture-output")]
public class ManufactureOutputController : ControllerBase
{
    private readonly IMediator _mediator;

    public ManufactureOutputController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<GetManufactureOutputResponse>> GetManufactureOutput(
        [FromQuery] int monthsBack = 13,
        CancellationToken cancellationToken = default)
    {
        var request = new GetManufactureOutputRequest { MonthsBack = monthsBack };
        var response = await _mediator.Send(request, cancellationToken);

        if (!response.Success)
        {
            return response.ErrorCode switch
            {
                ErrorCodes.InvalidAnalysisParameters => BadRequest(response),
                ErrorCodes.ManufacturingDataNotAvailable => NotFound(response),
                ErrorCodes.ManufactureAnalysisCalculationFailed => StatusCode(500, response),
                _ => StatusCode(500, response)
            };
        }

        return Ok(response);
    }
}