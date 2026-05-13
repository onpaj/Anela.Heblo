using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MeetingTasks;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.SubmitToTodo;

public class SubmitToTodoHandler : IRequestHandler<SubmitToTodoRequest, SubmitToTodoResponse>
{
    private readonly IMeetingTranscriptRepository _repository;
    private readonly IGraphTodoService _todoService;
    private readonly ILogger<SubmitToTodoHandler> _logger;

    public SubmitToTodoHandler(
        IMeetingTranscriptRepository repository,
        IGraphTodoService todoService,
        ILogger<SubmitToTodoHandler> logger)
    {
        _repository = repository;
        _todoService = todoService;
        _logger = logger;
    }

    public async Task<SubmitToTodoResponse> Handle(SubmitToTodoRequest request, CancellationToken cancellationToken)
    {
        var transcript = await _repository.GetByIdAsync(request.TranscriptId, cancellationToken);
        if (transcript is null)
        {
            _logger.LogWarning("SubmitToTodo: transcript {Id} not found", request.TranscriptId);
            return new SubmitToTodoResponse(ErrorCodes.ResourceNotFound);
        }

        var response = new SubmitToTodoResponse();

        var toSubmit = transcript.Tasks
            .Where(t => t.Status == ProposedTaskStatus.Approved && t.ExternalTaskId is null)
            .ToList();

        foreach (var task in toSubmit)
        {
            var userId = await _todoService.ResolveUserIdAsync(task.Assignee, cancellationToken);
            if (userId is null)
            {
                response.FailedCount++;
                response.Errors.Add($"Could not resolve assignee '{task.Assignee}' for task '{task.Title}'.");
                continue;
            }

            var result = await _todoService.CreateTodoTaskAsync(
                userId,
                task.Title,
                task.Description,
                task.DueDate,
                cancellationToken);

            if (result.Success && result.ExternalTaskId is not null)
            {
                task.ExternalTaskId = result.ExternalTaskId;
                // Per-task save: bounds blast radius if the process crashes mid-loop.
                // Without this, ExternalTaskId would be lost and the next /submit call would
                // recreate the Graph task — breaking idempotency.
                await _repository.SaveChangesAsync(cancellationToken);
                response.SuccessCount++;
            }
            else
            {
                response.FailedCount++;
                response.Errors.Add($"Failed to create TODO task '{task.Title}' for '{task.Assignee}': {result.Error}");
            }
        }

        // Status recompute. Tasks are "done" if Rejected (intentionally skipped) or have an ExternalTaskId.
        // Empty/all-rejected collections satisfy allDone vacuously — matches spec FR-3.
        var allDone = transcript.Tasks.All(t =>
            t.Status == ProposedTaskStatus.Rejected || t.ExternalTaskId is not null);
        var hasRejected = transcript.Tasks.Any(t => t.Status == ProposedTaskStatus.Rejected);

        transcript.Status = allDone
            ? (hasRejected ? MeetingTranscriptStatus.PartiallyApproved : MeetingTranscriptStatus.Approved)
            : MeetingTranscriptStatus.PendingReview;
        transcript.ReviewedAt = DateTime.UtcNow;

        await _repository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Submitted {SuccessCount} tasks to TODO for transcript {Id}, {FailedCount} failed",
            response.SuccessCount, transcript.Id, response.FailedCount);

        return response;
    }
}
