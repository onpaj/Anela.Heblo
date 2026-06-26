using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MeetingTasks;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.SubmitToTodo;

public class SubmitToTodoHandler : IRequestHandler<SubmitToTodoRequest, SubmitToTodoResponse>
{
    private readonly IMeetingTranscriptRepository _repository;
    private readonly IMeetingTaskExporter _taskExporter;
    private readonly IMeetingAccessGuard _accessGuard;
    private readonly ILogger<SubmitToTodoHandler> _logger;

    public SubmitToTodoHandler(
        IMeetingTranscriptRepository repository,
        IMeetingTaskExporter taskExporter,
        IMeetingAccessGuard accessGuard,
        ILogger<SubmitToTodoHandler> logger)
    {
        _repository = repository;
        _taskExporter = taskExporter;
        _accessGuard = accessGuard;
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

        if (!_accessGuard.CanAccess(transcript))
        {
            _logger.LogWarning("Access denied to meeting transcript {TranscriptId} for current user", request.TranscriptId);
            return new SubmitToTodoResponse(ErrorCodes.ResourceNotFound);
        }

        var response = new SubmitToTodoResponse();

        var toSubmit = transcript.Tasks
            .Where(t => t.Status == ProposedTaskStatus.Approved && t.ExternalTaskId is null)
            .ToList();

        foreach (var task in toSubmit)
        {
            if (string.IsNullOrWhiteSpace(task.AssigneeEmail))
            {
                response.FailedCount++;
                response.Errors.Add(
                    $"Task '{task.Title}' has no resolved user — assign a known user before submitting.");
                continue;
            }

            var userId = await _taskExporter.ResolveUserIdByEmailAsync(task.AssigneeEmail, cancellationToken);
            if (userId is null)
            {
                response.FailedCount++;
                response.Errors.Add(
                    $"Could not resolve user '{task.AssigneeEmail}' for task '{task.Title}'.");
                continue;
            }

            var result = await _taskExporter.ExportTaskAsync(
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
                // recreate the Planner task — breaking idempotency.
                await _repository.SaveChangesAsync(cancellationToken);
                response.SuccessCount++;
            }
            else
            {
                response.FailedCount++;
                response.Errors.Add($"Failed to export Planner task '{task.Title}' for '{task.AssigneeEmail}': {result.Error}");
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

        // Warning when anything failed — Information is dropped by CostOptimizedTelemetryProcessor
        // in Production, which previously hid silent consent failures from telemetry.
        if (response.FailedCount > 0)
        {
            _logger.LogWarning(
                "Exported {SuccessCount} Planner tasks for transcript {Id}, {FailedCount} failed",
                response.SuccessCount, transcript.Id, response.FailedCount);
        }
        else
        {
            _logger.LogInformation(
                "Exported {SuccessCount} Planner tasks for transcript {Id}, {FailedCount} failed",
                response.SuccessCount, transcript.Id, response.FailedCount);
        }

        return response;
    }
}
