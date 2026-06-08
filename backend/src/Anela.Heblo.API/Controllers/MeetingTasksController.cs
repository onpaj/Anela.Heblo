using Anela.Heblo.Application.Features.MeetingTasks.UseCases.AddProposedTask;
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.ExplainSummary;
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.GetMeetingUsers;
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.GetTranscriptDetail;
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.GetTranscriptList;
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.ReimportMeetingTranscript;
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.SubmitToTodo;
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.UpdateMeetingAccess;
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.UpdateProposedTask;
using Anela.Heblo.Application.Features.MeetingTasks.UseCases.UpdateProposedTaskStatus;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[FeatureAuthorize(Feature.Anela_Meetings)]
[ApiController]
[Route("api/meeting-tasks")]
public sealed class MeetingTasksController : BaseApiController
{
    private readonly IMediator _mediator;

    public MeetingTasksController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<GetTranscriptListResponse>> List(
        [FromQuery] GetTranscriptListRequest request,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(request, ct);
        return HandleResponse(result);
    }

    [HttpGet("users")]
    public async Task<ActionResult<GetMeetingUsersResponse>> Users(CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetMeetingUsersRequest(), ct);
        return HandleResponse(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GetTranscriptDetailResponse>> Detail(
        Guid id,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetTranscriptDetailRequest { Id = id }, ct);
        return HandleResponse(result);
    }

    [HttpPut("{transcriptId:guid}/tasks/{taskId:guid}")]
    [FeatureAuthorize(Feature.Anela_Meetings, AccessLevel.Write)]
    public async Task<ActionResult<UpdateProposedTaskResponse>> UpdateTask(
        Guid transcriptId,
        Guid taskId,
        [FromBody] UpdateProposedTaskRequest request,
        CancellationToken ct = default)
    {
        request.TranscriptId = transcriptId;
        request.TaskId = taskId;
        var result = await _mediator.Send(request, ct);
        return HandleResponse(result);
    }

    [HttpPut("{transcriptId:guid}/tasks/{taskId:guid}/status")]
    [FeatureAuthorize(Feature.Anela_Meetings, AccessLevel.Write)]
    public async Task<ActionResult<UpdateProposedTaskStatusResponse>> UpdateTaskStatus(
        Guid transcriptId,
        Guid taskId,
        [FromBody] UpdateProposedTaskStatusRequest request,
        CancellationToken ct = default)
    {
        request.TranscriptId = transcriptId;
        request.TaskId = taskId;
        var result = await _mediator.Send(request, ct);
        return HandleResponse(result);
    }

    [HttpPost("{transcriptId:guid}/tasks")]
    [FeatureAuthorize(Feature.Anela_Meetings, AccessLevel.Write)]
    public async Task<ActionResult<AddProposedTaskResponse>> AddTask(
        Guid transcriptId,
        [FromBody] AddProposedTaskRequest request,
        CancellationToken ct = default)
    {
        request.TranscriptId = transcriptId;
        var result = await _mediator.Send(request, ct);
        return HandleResponse(result);
    }

    [HttpPost("{transcriptId:guid}/submit")]
    [FeatureAuthorize(Feature.Anela_Meetings, AccessLevel.Write)]
    public async Task<ActionResult<SubmitToTodoResponse>> Submit(
        Guid transcriptId,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new SubmitToTodoRequest { TranscriptId = transcriptId }, ct);
        return HandleResponse(result);
    }

    [HttpPost("{transcriptId:guid}/explain")]
    [FeatureAuthorize(Feature.Anela_Meetings, AccessLevel.Write)]
    public async Task<ActionResult<ExplainSummaryResponse>> ExplainSummary(
        Guid transcriptId,
        [FromBody] ExplainSummaryRequest request,
        CancellationToken ct = default)
    {
        request.TranscriptId = transcriptId;
        var result = await _mediator.Send(request, ct);
        return HandleResponse(result);
    }

    [HttpPut("{transcriptId:guid}/access")]
    [FeatureAuthorize(Feature.Anela_Meetings, AccessLevel.Write)]
    public async Task<ActionResult<UpdateMeetingAccessResponse>> UpdateAccess(
        Guid transcriptId,
        [FromBody] UpdateMeetingAccessRequest request,
        CancellationToken ct = default)
    {
        request.TranscriptId = transcriptId;
        var result = await _mediator.Send(request, ct);
        return HandleResponse(result);
    }

    [HttpPost("{transcriptId:guid}/reimport")]
    [FeatureAuthorize(Feature.Anela_Meetings, AccessLevel.Write)]
    public async Task<ActionResult<ReimportMeetingTranscriptResponse>> Reimport(
        Guid transcriptId,
        CancellationToken ct = default)
        => HandleResponse(await _mediator.Send(new ReimportMeetingTranscriptRequest { Id = transcriptId }, ct));
}
