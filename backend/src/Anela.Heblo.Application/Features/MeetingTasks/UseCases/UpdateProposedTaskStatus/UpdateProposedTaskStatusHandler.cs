using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MeetingTasks;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.UpdateProposedTaskStatus;

public class UpdateProposedTaskStatusHandler : IRequestHandler<UpdateProposedTaskStatusRequest, UpdateProposedTaskStatusResponse>
{
    private readonly IMeetingTranscriptRepository _repository;
    private readonly IMeetingAccessGuard _accessGuard;
    private readonly ILogger<UpdateProposedTaskStatusHandler> _logger;

    public UpdateProposedTaskStatusHandler(
        IMeetingTranscriptRepository repository,
        IMeetingAccessGuard accessGuard,
        ILogger<UpdateProposedTaskStatusHandler> logger)
    {
        _repository = repository;
        _accessGuard = accessGuard;
        _logger = logger;
    }

    public async Task<UpdateProposedTaskStatusResponse> Handle(UpdateProposedTaskStatusRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Updating proposed task status — TranscriptId: {TranscriptId}, TaskId: {TaskId}, Status: {Status}",
            request.TranscriptId, request.TaskId, request.Status);

        var transcript = await _repository.GetByIdAsync(request.TranscriptId, cancellationToken);
        if (transcript is null)
        {
            _logger.LogWarning("Meeting transcript {TranscriptId} not found", request.TranscriptId);
            return new UpdateProposedTaskStatusResponse(ErrorCodes.ResourceNotFound);
        }

        if (!_accessGuard.CanAccess(transcript))
        {
            _logger.LogWarning("Access denied to meeting transcript {TranscriptId} for current user", request.TranscriptId);
            return new UpdateProposedTaskStatusResponse(ErrorCodes.ResourceNotFound);
        }

        var task = transcript.Tasks.FirstOrDefault(t => t.Id == request.TaskId);
        if (task is null)
        {
            _logger.LogWarning(
                "Proposed task {TaskId} not found on transcript {TranscriptId}",
                request.TaskId, request.TranscriptId);
            return new UpdateProposedTaskStatusResponse(ErrorCodes.ResourceNotFound);
        }

        if (!Enum.TryParse<ProposedTaskStatus>(request.Status, ignoreCase: true, out var newStatus))
        {
            _logger.LogWarning("Invalid proposed task status value: {Status}", request.Status);
            return new UpdateProposedTaskStatusResponse(ErrorCodes.ValidationError);
        }

        task.Status = newStatus;

        await _repository.SaveChangesAsync(cancellationToken);
        return new UpdateProposedTaskStatusResponse();
    }
}
