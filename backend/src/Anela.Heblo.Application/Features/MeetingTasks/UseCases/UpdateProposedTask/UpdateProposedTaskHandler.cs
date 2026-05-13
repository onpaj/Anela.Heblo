using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MeetingTasks;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.UpdateProposedTask;

public class UpdateProposedTaskHandler : IRequestHandler<UpdateProposedTaskRequest, UpdateProposedTaskResponse>
{
    private readonly IMeetingTranscriptRepository _repository;
    private readonly ILogger<UpdateProposedTaskHandler> _logger;

    public UpdateProposedTaskHandler(IMeetingTranscriptRepository repository, ILogger<UpdateProposedTaskHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<UpdateProposedTaskResponse> Handle(UpdateProposedTaskRequest request, CancellationToken cancellationToken)
    {
        var transcript = await _repository.GetByIdAsync(request.TranscriptId, cancellationToken);
        if (transcript is null)
            return new UpdateProposedTaskResponse(ErrorCodes.ResourceNotFound);

        var task = transcript.Tasks.FirstOrDefault(t => t.Id == request.TaskId);
        if (task is null)
            return new UpdateProposedTaskResponse(ErrorCodes.ResourceNotFound);

        task.Title = request.Title;
        task.Description = request.Description;
        task.Assignee = request.Assignee;
        task.DueDate = request.DueDate;

        await _repository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated task {TaskId} on transcript {TranscriptId}", request.TaskId, request.TranscriptId);
        return new UpdateProposedTaskResponse();
    }
}
