using Anela.Heblo.Application.Features.MeetingTasks.Contracts;
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MeetingTasks;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.GetTranscriptDetail;

public class GetTranscriptDetailHandler : IRequestHandler<GetTranscriptDetailRequest, GetTranscriptDetailResponse>
{
    private readonly IMeetingTranscriptRepository _repository;
    private readonly IMeetingAccessGuard _accessGuard;
    private readonly ILogger<GetTranscriptDetailHandler> _logger;

    public GetTranscriptDetailHandler(
        IMeetingTranscriptRepository repository,
        IMeetingAccessGuard accessGuard,
        ILogger<GetTranscriptDetailHandler> logger)
    {
        _repository = repository;
        _accessGuard = accessGuard;
        _logger = logger;
    }

    public async Task<GetTranscriptDetailResponse> Handle(GetTranscriptDetailRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting meeting transcript detail — Id: {Id}", request.Id);

        var transcript = await _repository.GetByIdAsync(request.Id, cancellationToken);

        if (transcript is null)
        {
            _logger.LogWarning("Meeting transcript {Id} not found", request.Id);
            return new GetTranscriptDetailResponse(ErrorCodes.ResourceNotFound);
        }

        if (!_accessGuard.CanAccess(transcript))
        {
            _logger.LogWarning("Access denied to meeting transcript {TranscriptId} for current user", request.Id);
            return new GetTranscriptDetailResponse(ErrorCodes.ResourceNotFound);
        }

        var dto = new MeetingTranscriptDto
        {
            Id = transcript.Id,
            PlaudRecordingId = transcript.PlaudRecordingId,
            PlaudCreatedAt = transcript.PlaudCreatedAt,
            Subject = transcript.Subject,
            Summary = transcript.Summary,
            RawTranscript = transcript.RawTranscript,
            Status = transcript.Status.ToString(),
            ReceivedAt = transcript.ReceivedAt,
            ReviewedAt = transcript.ReviewedAt,
            ReviewedByUser = transcript.ReviewedByUser,
            TaskCount = transcript.Tasks.Count,
            ApprovedTaskCount = transcript.Tasks.Count(x => x.Status == ProposedTaskStatus.Approved),
            RejectedTaskCount = transcript.Tasks.Count(x => x.Status == ProposedTaskStatus.Rejected),
            Tasks = transcript.Tasks.Select(t => new ProposedTaskDto
            {
                Id = t.Id,
                Title = t.Title,
                Description = t.Description,
                Assignee = t.Assignee,
                AssigneeEmail = t.AssigneeEmail,
                DueDate = t.DueDate,
                Status = t.Status.ToString(),
                ExternalTaskId = t.ExternalTaskId,
                IsManuallyAdded = t.IsManuallyAdded
            }).ToList(),
            AccessLevel = transcript.AccessLevel.ToString(),
            AccessGrants = transcript.AccessGrants.Select(g => new MeetingAccessGrantDto
            {
                UserEmail = g.UserEmail,
                UserDisplayName = g.UserDisplayName
            }).ToList(),
        };

        return new GetTranscriptDetailResponse { Transcript = dto };
    }
}
