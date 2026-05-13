using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MeetingTasks;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.AddProposedTask;

public class AddProposedTaskHandler : IRequestHandler<AddProposedTaskRequest, AddProposedTaskResponse>
{
    private readonly IMeetingTranscriptRepository _repository;
    private readonly ILogger<AddProposedTaskHandler> _logger;

    public AddProposedTaskHandler(IMeetingTranscriptRepository repository, ILogger<AddProposedTaskHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<AddProposedTaskResponse> Handle(AddProposedTaskRequest request, CancellationToken cancellationToken)
    {
        var transcript = await _repository.GetByIdAsync(request.TranscriptId, cancellationToken);
        if (transcript is null)
            return new AddProposedTaskResponse(ErrorCodes.ResourceNotFound);

        var newTask = new ProposedTask
        {
            Id = Guid.NewGuid(),
            MeetingTranscriptId = request.TranscriptId,
            Title = request.Title,
            Description = request.Description,
            Assignee = request.Assignee,
            DueDate = request.DueDate,
            Status = ProposedTaskStatus.Pending,
            IsManuallyAdded = true
        };

        transcript.Tasks.Add(newTask);
        await _repository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Added manual task {TaskId} to transcript {TranscriptId}", newTask.Id, request.TranscriptId);
        return new AddProposedTaskResponse { TaskId = newTask.Id };
    }
}
