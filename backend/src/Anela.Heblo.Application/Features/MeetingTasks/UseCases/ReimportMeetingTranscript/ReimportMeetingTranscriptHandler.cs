using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MeetingTasks;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.ReimportMeetingTranscript;

public sealed class ReimportMeetingTranscriptHandler
    : IRequestHandler<ReimportMeetingTranscriptRequest, ReimportMeetingTranscriptResponse>
{
    private readonly IMeetingTranscriptRepository _repository;
    private readonly IPlaudClient _plaudClient;
    private readonly IMeetingAccessGuard _accessGuard;
    private readonly IMeetingTaskExtractor _extractor;
    private readonly IMeetingUserDirectory _userDirectory;
    private readonly ILogger<ReimportMeetingTranscriptHandler> _logger;

    public ReimportMeetingTranscriptHandler(
        IMeetingTranscriptRepository repository,
        IPlaudClient plaudClient,
        IMeetingAccessGuard accessGuard,
        IMeetingTaskExtractor extractor,
        IMeetingUserDirectory userDirectory,
        ILogger<ReimportMeetingTranscriptHandler> logger)
    {
        _repository = repository;
        _plaudClient = plaudClient;
        _accessGuard = accessGuard;
        _extractor = extractor;
        _userDirectory = userDirectory;
        _logger = logger;
    }

    public async Task<ReimportMeetingTranscriptResponse> Handle(
        ReimportMeetingTranscriptRequest request,
        CancellationToken cancellationToken)
    {
        var transcript = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (transcript is null)
        {
            _logger.LogWarning("Meeting transcript {Id} not found for reimport", request.Id);
            return new ReimportMeetingTranscriptResponse(ErrorCodes.ResourceNotFound);
        }

        if (!_accessGuard.CanAccess(transcript))
        {
            _logger.LogWarning("Access denied to meeting transcript {Id} for reimport", request.Id);
            return new ReimportMeetingTranscriptResponse(ErrorCodes.ResourceNotFound);
        }

        var detail = await _plaudClient.GetFileDetailAsync(transcript.PlaudRecordingId, cancellationToken);
        if (!detail.IsGenerated)
        {
            _logger.LogInformation("Recording {RecordingId} not yet generated on Plaud, cannot reimport", transcript.PlaudRecordingId);
            return new ReimportMeetingTranscriptResponse(ErrorCodes.BusinessRuleViolation);
        }

        var rawTranscript = await _plaudClient.GetTranscriptAsync(transcript.PlaudRecordingId, cancellationToken);
        var summaryResult = await _plaudClient.GetSummaryAsync(transcript.PlaudRecordingId, cancellationToken);

        transcript.RawTranscript = rawTranscript;
        transcript.Summary = summaryResult.MarkdownContent;
        if (!string.IsNullOrWhiteSpace(summaryResult.Headline))
            transcript.Subject = summaryResult.Headline;

        var extractedTasks = await _extractor.ExtractAsync(summaryResult.MarkdownContent, rawTranscript, cancellationToken);
        var newTasks = extractedTasks
            .Select(t => new ProposedTask
            {
                Id = Guid.NewGuid(),
                MeetingTranscriptId = transcript.Id,
                Title = t.Title,
                Description = t.Description,
                Assignee = t.Assignee,
                AssigneeEmail = ResolveAssigneeEmail(t),
                DueDate = t.DueDate,
                Status = ProposedTaskStatus.Pending,
                IsManuallyAdded = false
            })
            .ToList();

        await _repository.ReplacePendingTasksAsync(transcript, newTasks, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Reimported recording {RecordingId} for transcript {TranscriptId} with {TaskCount} tasks",
            transcript.PlaudRecordingId, transcript.Id, newTasks.Count);

        return new ReimportMeetingTranscriptResponse();
    }

    private string? ResolveAssigneeEmail(ExtractedTask task)
    {
        if (!string.IsNullOrWhiteSpace(task.AssigneeEmail))
            return task.AssigneeEmail;

        return _userDirectory.Resolve(task.Assignee)?.Email;
    }
}
