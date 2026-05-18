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
    private readonly ILogger<ReimportMeetingTranscriptHandler> _logger;

    public ReimportMeetingTranscriptHandler(
        IMeetingTranscriptRepository repository,
        IPlaudClient plaudClient,
        IMeetingAccessGuard accessGuard,
        ILogger<ReimportMeetingTranscriptHandler> logger)
    {
        _repository = repository;
        _plaudClient = plaudClient;
        _accessGuard = accessGuard;
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

        string? recordingName = null;
        try
        {
            const int maxLookbackDays = 365; // Plaud's ListRecent window cap
            var days = Math.Clamp(
                (int)(DateTime.UtcNow.Date - transcript.PlaudCreatedAt.Date).TotalDays + 1,
                1, maxLookbackDays);
            var recent = await _plaudClient.ListRecentAsync(days, cancellationToken);
            recordingName = recent.FirstOrDefault(r => r.Id == transcript.PlaudRecordingId)?.Name;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch recording name for {RecordingId}, falling back to headline",
                transcript.PlaudRecordingId);
        }

        transcript.RawTranscript = rawTranscript;
        transcript.Summary = summaryResult.MarkdownContent;

        if (!string.IsNullOrWhiteSpace(recordingName))
            transcript.Subject = recordingName;
        else if (!string.IsNullOrWhiteSpace(summaryResult.Headline))
            transcript.Subject = summaryResult.Headline;
        // else: leave transcript.Subject unchanged

        await _repository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Reimported recording {RecordingId} for transcript {TranscriptId}", transcript.PlaudRecordingId, transcript.Id);

        return new ReimportMeetingTranscriptResponse();
    }
}
