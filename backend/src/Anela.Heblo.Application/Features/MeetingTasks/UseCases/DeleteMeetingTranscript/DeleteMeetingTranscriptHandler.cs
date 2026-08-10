using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MeetingTasks;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.DeleteMeetingTranscript;

public class DeleteMeetingTranscriptHandler : IRequestHandler<DeleteMeetingTranscriptRequest, DeleteMeetingTranscriptResponse>
{
    private readonly IMeetingTranscriptRepository _repository;
    private readonly IMeetingAccessGuard _accessGuard;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<DeleteMeetingTranscriptHandler> _logger;

    public DeleteMeetingTranscriptHandler(
        IMeetingTranscriptRepository repository,
        IMeetingAccessGuard accessGuard,
        ICurrentUserService currentUserService,
        ILogger<DeleteMeetingTranscriptHandler> logger)
    {
        _repository = repository;
        _accessGuard = accessGuard;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<DeleteMeetingTranscriptResponse> Handle(
        DeleteMeetingTranscriptRequest request,
        CancellationToken cancellationToken)
    {
        if (!_accessGuard.IsManager())
        {
            _logger.LogWarning("Non-manager attempted to delete meeting transcript {TranscriptId}", request.TranscriptId);
            return new DeleteMeetingTranscriptResponse(ErrorCodes.Forbidden);
        }

        var transcript = await _repository.GetByIdAsync(request.TranscriptId, cancellationToken);
        if (transcript is null)
        {
            _logger.LogWarning("Meeting transcript {TranscriptId} not found", request.TranscriptId);
            return new DeleteMeetingTranscriptResponse(ErrorCodes.ResourceNotFound);
        }

        var userEmail = _currentUserService.GetCurrentUser().Email ?? string.Empty;
        var plaudRecordingId = transcript.PlaudRecordingId;

        await _repository.DeleteAsync(transcript, userEmail, cancellationToken);

        _logger.LogWarning(
            "Meeting transcript {TranscriptId} (plaud {PlaudRecordingId}) deleted by {User}",
            request.TranscriptId, plaudRecordingId, userEmail);

        return new DeleteMeetingTranscriptResponse();
    }
}
