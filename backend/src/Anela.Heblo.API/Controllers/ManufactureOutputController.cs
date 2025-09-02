using Anela.Heblo.Application.Features.Manufacture.Model;
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
        try
        {
            var request = new GetManufactureOutputRequest { MonthsBack = monthsBack };
            var response = await _mediator.Send(request, cancellationToken);
            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            return StatusCode(500, ex.Message);
        }
    }
}