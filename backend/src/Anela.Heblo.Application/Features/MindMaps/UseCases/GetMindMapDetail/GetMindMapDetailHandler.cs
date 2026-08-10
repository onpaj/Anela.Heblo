using Anela.Heblo.Application.Features.MindMaps.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MindMaps;
using MediatR;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.GetMindMapDetail;

public class GetMindMapDetailHandler : IRequestHandler<GetMindMapDetailRequest, GetMindMapDetailResponse>
{
    private readonly IMindMapRepository _repository;

    public GetMindMapDetailHandler(IMindMapRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetMindMapDetailResponse> Handle(GetMindMapDetailRequest request, CancellationToken cancellationToken)
    {
        var map = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (map is null)
        {
            return new GetMindMapDetailResponse(ErrorCodes.ResourceNotFound);
        }

        var subjectsByMeetingId = map.Meetings
            .Where(m => m.MeetingTranscript != null)
            .ToDictionary(m => m.MeetingTranscriptId, m => m.MeetingTranscript.Subject);

        return new GetMindMapDetailResponse
        {
            Id = map.Id,
            Name = map.Name,
            Description = map.Description,
            Status = map.Status.ToString(),
            LastError = map.LastError,
            DocumentJson = map.CurrentJson,
            Meetings = map.Meetings
                .Where(m => m.MeetingTranscript != null)
                .OrderByDescending(m => m.MeetingTranscript.PlaudCreatedAt)
                .Select(m => new AttachedMeetingDto
                {
                    MeetingTranscriptId = m.MeetingTranscriptId,
                    Subject = m.MeetingTranscript.Subject,
                    PlaudCreatedAt = m.MeetingTranscript.PlaudCreatedAt,
                    AttachedAt = m.AttachedAt,
                    ProcessedAt = m.ProcessedAt
                }).ToList(),
            Versions = map.Versions
                .OrderByDescending(v => v.VersionNumber)
                .Select(v => new MindMapVersionDto
                {
                    VersionNumber = v.VersionNumber,
                    CreatedAt = v.CreatedAt,
                    TriggerMeetingId = v.TriggerMeetingId,
                    TriggerMeetingSubject = v.TriggerMeetingId != null
                        && subjectsByMeetingId.TryGetValue(v.TriggerMeetingId.Value, out var subject)
                            ? subject
                            : null
                }).ToList()
        };
    }
}
