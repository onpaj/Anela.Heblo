using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MeetingTasks;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.UpdateProposedTask;

public class UpdateProposedTaskHandler : IRequestHandler<UpdateProposedTaskRequest, UpdateProposedTaskResponse>
{
    private readonly IMeetingTranscriptRepository _repository;
    private readonly IMeetingAccessGuard _accessGuard;
    private readonly ILogger<UpdateProposedTaskHandler> _logger;

    public UpdateProposedTaskHandler(
        IMeetingTranscriptRepository repository,
        IMeetingAccessGuard accessGuard,
        ILogger<UpdateProposedTaskHandler> logger)
    {
        _repository = repository;
        _accessGuard = accessGuard;
        _logger = logger;
    }

    public async Task<UpdateProposedTaskResponse> Handle(UpdateProposedTaskRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Updating proposed task — TranscriptId: {TranscriptId}, TaskId: {TaskId}",
            request.TranscriptId, request.TaskId);

        var transcript = await _repository.GetByIdAsync(request.TranscriptId, cancellationToken);
        if (transcript is null)
        {
            _logger.LogWarning("Meeting transcript {TranscriptId} not found", request.TranscriptId);
            return new UpdateProposedTaskResponse(ErrorCodes.ResourceNotFound);
        }

        if (!_accessGuard.CanAccess(transcript))
        {
            _logger.LogWarning("Access denied to meeting transcript {TranscriptId} for current user", request.TranscriptId);
            return new UpdateProposedTaskResponse(ErrorCodes.ResourceNotFound);
        }

        var task = transcript.Tasks.FirstOrDefault(t => t.Id == request.TaskId);
        if (task is null)
        {
            _logger.LogWarning(
                "Proposed task {TaskId} not found on transcript {TranscriptId}",
                request.TaskId, request.TranscriptId);
            return new UpdateProposedTaskResponse(ErrorCodes.ResourceNotFound);
        }

        task.Title = request.Title;
        task.Description = request.Description;
        task.Assignee = request.Assignee;
        task.AssigneeEmail = request.AssigneeEmail;
        task.DueDate = request.DueDate;

        await _repository.SaveChangesAsync(cancellationToken);
        return new UpdateProposedTaskResponse();
    }
}
