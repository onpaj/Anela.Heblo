using Anela.Heblo.Application.Features.Smartsupp.UseCases.CloseConversation;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.GenerateDraftReply;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.GetContactShoptetInfo;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.SendMessage;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.GetConversation;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.GetVisitorInfo;
using Anela.Heblo.Application.Features.Smartsupp.UseCases.ListConversations;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[ApiController]
[Route("api/smartsupp")]
[Authorize]
public class SmartsuppController : BaseApiController
{
    private readonly IMediator _mediator;

    public SmartsuppController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("conversations")]
    [ProducesResponseType(typeof(ListConversationsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ListConversationsResponse>> GetConversations(
        [FromQuery] string status = "Open",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var request = new ListConversationsRequest { Status = status, Page = page, PageSize = pageSize };
        var result = await _mediator.Send(request, cancellationToken);
        return HandleResponse(result);
    }

    [HttpGet("conversations/{id}")]
    [ProducesResponseType(typeof(GetConversationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetConversationResponse>> GetConversation(
        string id,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetConversationRequest { Id = id }, cancellationToken);
        return HandleResponse(result);
    }

    [HttpPost("conversations/{id}/draft-reply")]
    [ProducesResponseType(typeof(GenerateDraftReplyResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<GenerateDraftReplyResponse>> GenerateDraftReply(
        string id,
        [FromBody] GenerateDraftReplyBody? body,
        CancellationToken cancellationToken = default)
    {
        var request = new GenerateDraftReplyRequest { ConversationId = id, Topic = body?.Topic };
        var result = await _mediator.Send(request, cancellationToken);
        return HandleResponse(result);
    }

    /// <remarks>
    /// Returns 404 only when the Smartsupp conversation itself is not found.
    /// A missing Shoptet customer returns 200 with a null contactInfo payload.
    /// </remarks>
    [HttpGet("conversations/{id}/shoptet-info")]
    [ProducesResponseType(typeof(GetSmartsuppContactShoptetInfoResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetSmartsuppContactShoptetInfoResponse>> GetShoptetInfo(
        string id,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetSmartsuppContactShoptetInfoRequest { ConversationId = id },
            cancellationToken);
        return HandleResponse(result);
    }

    [HttpGet("conversations/{id}/visitor-info")]
    [ProducesResponseType(typeof(GetVisitorInfoResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetVisitorInfoResponse>> GetVisitorInfo(
        string id,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetVisitorInfoRequest { ConversationId = id },
            cancellationToken);
        return HandleResponse(result);
    }

    [HttpPost("conversations/{conversationId}/messages")]
    [ProducesResponseType(typeof(SendMessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<SendMessageResponse>> SendMessage(
        string conversationId,
        [FromBody] SendMessageBody body,
        CancellationToken cancellationToken = default)
    {
        var request = new SendMessageRequest { ConversationId = conversationId, Content = body.Content };
        var result = await _mediator.Send(request, cancellationToken);
        return HandleResponse(result);
    }

    [HttpPost("conversations/{id}/close")]
    [ProducesResponseType(typeof(CloseConversationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<CloseConversationResponse>> CloseConversation(
        string id,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new CloseConversationRequest { ConversationId = id },
            cancellationToken);
        return HandleResponse(result);
    }

}

public sealed class GenerateDraftReplyBody
{
    public string? Topic { get; set; }
}

public sealed class SendMessageBody
{
    public string Content { get; set; } = string.Empty;
}
