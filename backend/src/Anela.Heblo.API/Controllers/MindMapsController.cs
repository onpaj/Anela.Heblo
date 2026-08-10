using Anela.Heblo.Application.Features.MindMaps.UseCases.AttachMeeting;
using Anela.Heblo.Application.Features.MindMaps.UseCases.CreateMindMap;
using Anela.Heblo.Application.Features.MindMaps.UseCases.DeleteMindMap;
using Anela.Heblo.Application.Features.MindMaps.UseCases.DetachMeeting;
using Anela.Heblo.Application.Features.MindMaps.UseCases.GetMindMapDetail;
using Anela.Heblo.Application.Features.MindMaps.UseCases.GetMindMapList;
using Anela.Heblo.Application.Features.MindMaps.UseCases.RegenerateMindMap;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[FeatureAuthorize(Feature.Anela_MindMaps)]
[ApiController]
[Route("api/mind-maps")]
public sealed class MindMapsController : BaseApiController
{
    private readonly IMediator _mediator;

    public MindMapsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<GetMindMapListResponse>> List(CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetMindMapListRequest(), ct);
        return HandleResponse(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GetMindMapDetailResponse>> Detail(Guid id, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetMindMapDetailRequest { Id = id }, ct);
        return HandleResponse(result);
    }

    [HttpPost]
    [FeatureAuthorize(Feature.Anela_MindMaps, AccessLevel.Write)]
    public async Task<ActionResult<CreateMindMapResponse>> Create(
        [FromBody] CreateMindMapRequest request,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(request, ct);
        return HandleResponse(result);
    }

    [HttpDelete("{id:guid}")]
    [FeatureAuthorize(Feature.Anela_MindMaps, AccessLevel.Write)]
    public async Task<ActionResult<DeleteMindMapResponse>> Delete(Guid id, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new DeleteMindMapRequest { Id = id }, ct);
        return HandleResponse(result);
    }

    [HttpPost("{id:guid}/meetings")]
    [FeatureAuthorize(Feature.Anela_MindMaps, AccessLevel.Write)]
    public async Task<ActionResult<AttachMeetingResponse>> AttachMeeting(
        Guid id,
        [FromBody] AttachMeetingRequest request,
        CancellationToken ct = default)
    {
        request.MindMapId = id;
        var result = await _mediator.Send(request, ct);
        return HandleResponse(result);
    }

    [HttpDelete("{id:guid}/meetings/{meetingId:guid}")]
    [FeatureAuthorize(Feature.Anela_MindMaps, AccessLevel.Write)]
    public async Task<ActionResult<DetachMeetingResponse>> DetachMeeting(
        Guid id,
        Guid meetingId,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(
            new DetachMeetingRequest { MindMapId = id, MeetingTranscriptId = meetingId }, ct);
        return HandleResponse(result);
    }

    [HttpPost("{id:guid}/regenerate")]
    [FeatureAuthorize(Feature.Anela_MindMaps, AccessLevel.Write)]
    public async Task<ActionResult<RegenerateMindMapResponse>> Regenerate(Guid id, CancellationToken ct = default)
    {
        var result = await _mediator.Send(new RegenerateMindMapRequest { Id = id }, ct);
        return HandleResponse(result);
    }
}
