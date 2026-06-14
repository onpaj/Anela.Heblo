using Anela.Heblo.Application.Features.ExpeditionList.UseCases.RunExpeditionListPrintFix;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[FeatureAuthorize(Feature.Warehouse_Expedition)]
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
    [FeatureAuthorize(Feature.Warehouse_Expedition, AccessLevel.Write)]
    public async Task<ActionResult<RunExpeditionListPrintFixResponse>> RunFix(CancellationToken cancellationToken)
    {
        var request = new RunExpeditionListPrintFixRequest();
        var response = await _mediator.Send(request, cancellationToken);
        return Ok(response);
    }
}
