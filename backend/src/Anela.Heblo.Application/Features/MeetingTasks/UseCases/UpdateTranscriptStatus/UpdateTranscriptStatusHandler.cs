using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MeetingTasks;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.UpdateTranscriptStatus;

public class UpdateTranscriptStatusHandler : IRequestHandler<UpdateTranscriptStatusRequest, UpdateTranscriptStatusResponse>
{
    // The manual review toggle only moves between these two states. PartiallyApproved is a
    // derived outcome of the submit-to-todo flow and cannot be set directly by the user.
    private static readonly MeetingTranscriptStatus[] ManualStatuses =
    {
        MeetingTranscriptStatus.PendingReview,
        MeetingTranscriptStatus.Approved,
    };

    private readonly IMeetingTranscriptRepository _repository;
    private readonly IMeetingAccessGuard _accessGuard;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<UpdateTranscriptStatusHandler> _logger;

    public UpdateTranscriptStatusHandler(
        IMeetingTranscriptRepository repository,
        IMeetingAccessGuard accessGuard,
        ICurrentUserService currentUserService,
        ILogger<UpdateTranscriptStatusHandler> logger)
    {
        _repository = repository;
        _accessGuard = accessGuard;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<UpdateTranscriptStatusResponse> Handle(UpdateTranscriptStatusRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Updating meeting transcript status — TranscriptId: {TranscriptId}, Status: {Status}",
            request.TranscriptId, request.Status);

        var transcript = await _repository.GetByIdAsync(request.TranscriptId, cancellationToken);
        if (transcript is null)
        {
            _logger.LogWarning("Meeting transcript {TranscriptId} not found", request.TranscriptId);
            return new UpdateTranscriptStatusResponse(ErrorCodes.ResourceNotFound);
        }

        if (!_accessGuard.CanAccess(transcript))
        {
            _logger.LogWarning("Access denied to meeting transcript {TranscriptId} for current user", request.TranscriptId);
            return new UpdateTranscriptStatusResponse(ErrorCodes.ResourceNotFound);
        }

        if (!Enum.TryParse<MeetingTranscriptStatus>(request.Status, ignoreCase: true, out var status)
            || !ManualStatuses.Contains(status))
        {
            _logger.LogWarning("Invalid meeting transcript status value: {Status}", request.Status);
            return new UpdateTranscriptStatusResponse(ErrorCodes.ValidationError,
                new Dictionary<string, string> { ["status"] = $"Unknown or non-toggleable status: {request.Status}" });
        }

        transcript.Status = status;
        if (status == MeetingTranscriptStatus.Approved)
        {
            transcript.ReviewedAt = DateTime.UtcNow;
            transcript.ReviewedByUser = _currentUserService.GetCurrentUser().Email ?? string.Empty;
        }
        else
        {
            transcript.ReviewedAt = null;
            transcript.ReviewedByUser = null;
        }

        await _repository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Meeting transcript status updated — TranscriptId: {TranscriptId}, Status: {Status}",
            request.TranscriptId, status);

        return new UpdateTranscriptStatusResponse { Status = status.ToString() };
    }
}
