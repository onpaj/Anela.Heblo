using Anela.Heblo.Application.Features.MeetingTasks.Contracts;
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MeetingTasks;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.AddProposedTask;

public class AddProposedTaskHandler : IRequestHandler<AddProposedTaskRequest, AddProposedTaskResponse>
{
    private readonly IMeetingTranscriptRepository _repository;
    private readonly IMeetingAccessGuard _accessGuard;
    private readonly ILogger<AddProposedTaskHandler> _logger;

    public AddProposedTaskHandler(
        IMeetingTranscriptRepository repository,
        IMeetingAccessGuard accessGuard,
        ILogger<AddProposedTaskHandler> logger)
    {
        _repository = repository;
        _accessGuard = accessGuard;
        _logger = logger;
    }

    public async Task<AddProposedTaskResponse> Handle(
        AddProposedTaskRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Adding manual proposed task — TranscriptId: {TranscriptId}, Title: {Title}",
            request.TranscriptId,
            request.Title);

        var transcript = await _repository.GetByIdAsync(request.TranscriptId, cancellationToken);
        if (transcript is null)
        {
            _logger.LogWarning("Meeting transcript {TranscriptId} not found", request.TranscriptId);
            return new AddProposedTaskResponse(ErrorCodes.ResourceNotFound);
        }

        if (!_accessGuard.CanAccess(transcript))
        {
            _logger.LogWarning("Access denied to meeting transcript {TranscriptId} for current user", request.TranscriptId);
            return new AddProposedTaskResponse(ErrorCodes.ResourceNotFound);
        }

        var task = new ProposedTask
        {
            Id = Guid.NewGuid(),
            MeetingTranscriptId = transcript.Id,
            Title = request.Title,
            Description = request.Description,
            Assignee = request.Assignee,
            AssigneeEmail = request.AssigneeEmail,
            DueDate = request.DueDate,
            Status = ProposedTaskStatus.Pending,
            ExternalTaskId = null,
            IsManuallyAdded = true
        };

        transcript.Tasks.Add(task);

        await _repository.SaveChangesAsync(cancellationToken);

        return new AddProposedTaskResponse
        {
            Task = new ProposedTaskDto
            {
                Id = task.Id,
                Title = task.Title,
                Description = task.Description,
                Assignee = task.Assignee,
                AssigneeEmail = task.AssigneeEmail,
                DueDate = task.DueDate,
                Status = task.Status.ToString(),
                ExternalTaskId = task.ExternalTaskId,
                IsManuallyAdded = task.IsManuallyAdded
            }
        };
    }
}
