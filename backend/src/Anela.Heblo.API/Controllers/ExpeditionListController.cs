using Anela.Heblo.Application.Features.ExpeditionList.UseCases.RunExpeditionListPrintFix;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[Authorize]
[ApiController]
[Route("api/expedition-list")]
public class ExpeditionListController : BaseApiController
{
    private readonly IMediator _mediator;

    public ExpeditionListController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("run-fix")]
    public async Task<ActionResult<RunExpeditionListPrintFixResponse>> RunFix(CancellationToken cancellationToken)
    {
        var request = new RunExpeditionListPrintFixRequest();
        var response = await _mediator.Send(request, cancellationToken);
        return Ok(response);
    }
}
