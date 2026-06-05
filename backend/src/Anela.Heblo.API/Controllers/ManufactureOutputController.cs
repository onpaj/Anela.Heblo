using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureOutput;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[Authorize(Roles = AccessRoles.ManufactureOutputRead)]
[ApiController]
[Route("api/manufacture-output")]
public class ManufactureOutputController : BaseApiController
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

        return HandleResponse(response);
    }
}