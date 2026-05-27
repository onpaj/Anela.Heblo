using Anela.Heblo.Application.Features.Smartsupp.UseCases.GetWebhookAuditEntry;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ListWebhookAudit;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ReplayWebhookEvent;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Smartsupp;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[ApiController]
[Route("api/admin/smartsupp/webhooks")]
[Authorize(Roles = AuthorizationConstants.Roles.SuperUser)]
public class SmartsuppWebhookAuditController : BaseApiController
{
    private readonly IMediator _mediator;

    public SmartsuppWebhookAuditController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ListWebhookAuditResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ListWebhookAuditResponse>> List(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? eventName,
        [FromQuery] SmartsuppWebhookSignatureStatus? signatureStatus,
        [FromQuery] SmartsuppWebhookProcessingStatus? processingStatus,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new ListWebhookAuditRequest
        {
            From = from,
            To = to,
            EventName = eventName,
            SignatureStatus = signatureStatus,
            ProcessingStatus = processingStatus,
            Skip = skip,
            Take = take,
        }, cancellationToken);
        return HandleResponse(response);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(GetWebhookAuditEntryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetWebhookAuditEntryResponse>> Get(
        Guid id,
        CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new GetWebhookAuditEntryRequest { Id = id }, cancellationToken);
        return HandleResponse(response);
    }

    [HttpPost("{id:guid}/replay")]
    [ProducesResponseType(typeof(ReplayWebhookEventResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReplayWebhookEventResponse>> Replay(
        Guid id,
        CancellationToken cancellationToken)
    {
        var replayedBy = User.Identity?.Name ?? "unknown";
        var response = await _mediator.Send(new ReplayWebhookEventRequest
        {
            Id = id,
            ReplayedBy = replayedBy,
        }, cancellationToken);
        return HandleResponse(response);
    }
}
