using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MeetingTasks;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.UpdateProposedTaskStatus;

public class UpdateProposedTaskStatusHandler : IRequestHandler<UpdateProposedTaskStatusRequest, UpdateProposedTaskStatusResponse>
{
    private readonly IMeetingTranscriptRepository _repository;
    private readonly ILogger<UpdateProposedTaskStatusHandler> _logger;

    public UpdateProposedTaskStatusHandler(IMeetingTranscriptRepository repository, ILogger<UpdateProposedTaskStatusHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<UpdateProposedTaskStatusResponse> Handle(UpdateProposedTaskStatusRequest request, CancellationToken cancellationToken)
    {
        var transcript = await _repository.GetByIdAsync(request.TranscriptId, cancellationToken);
        if (transcript is null)
            return new UpdateProposedTaskStatusResponse(ErrorCodes.ResourceNotFound);

        var task = transcript.Tasks.FirstOrDefault(t => t.Id == request.TaskId);
        if (task is null)
            return new UpdateProposedTaskStatusResponse(ErrorCodes.ResourceNotFound);

        if (!Enum.TryParse<ProposedTaskStatus>(request.Status, ignoreCase: true, out var newStatus))
            return new UpdateProposedTaskStatusResponse(ErrorCodes.ValidationError);

        task.Status = newStatus;
        await _repository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated status of task {TaskId} to {Status} on transcript {TranscriptId}", request.TaskId, newStatus, request.TranscriptId);
        return new UpdateProposedTaskStatusResponse();
    }
}
