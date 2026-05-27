using Anela.Heblo.Application.Features.MeetingTasks.Contracts;
using Anela.Heblo.Application.Features.MeetingTasks.Services;
using Anela.Heblo.Domain.Features.MeetingTasks;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MeetingTasks.UseCases.GetTranscriptList;

public class GetTranscriptListHandler : IRequestHandler<GetTranscriptListRequest, GetTranscriptListResponse>
{
    private readonly IMeetingTranscriptRepository _repository;
    private readonly IMeetingAccessGuard _accessGuard;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<GetTranscriptListHandler> _logger;

    public GetTranscriptListHandler(
        IMeetingTranscriptRepository repository,
        IMeetingAccessGuard accessGuard,
        ICurrentUserService currentUserService,
        ILogger<GetTranscriptListHandler> logger)
    {
        _repository = repository;
        _accessGuard = accessGuard;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<GetTranscriptListResponse> Handle(GetTranscriptListRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Getting meeting transcript list — StatusFilter: {StatusFilter}, PageNumber: {PageNumber}, PageSize: {PageSize}",
            request.StatusFilter, request.PageNumber, request.PageSize);

        MeetingTranscriptStatus? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(request.StatusFilter)
            && Enum.TryParse<MeetingTranscriptStatus>(request.StatusFilter, ignoreCase: true, out var parsed))
        {
            statusFilter = parsed;
        }

        var isManager = _accessGuard.IsManager();
        var userEmail = _currentUserService.GetCurrentUser().Email;

        var (items, totalCount) = await _repository.GetListAsync(
            statusFilter,
            isManager,
            userEmail,
            request.PageNumber,
            request.PageSize,
            cancellationToken);

        var dtos = items.Select(t => new MeetingTranscriptDto
        {
            Id = t.Id,
            PlaudRecordingId = t.PlaudRecordingId,
            PlaudCreatedAt = t.PlaudCreatedAt,
            Subject = t.Subject,
            Summary = t.Summary,
            Status = t.Status.ToString(),
            ReceivedAt = t.ReceivedAt,
            ReviewedAt = t.ReviewedAt,
            ReviewedByUser = t.ReviewedByUser,
            TaskCount = t.Tasks.Count,
            ApprovedTaskCount = t.Tasks.Count(x => x.Status == ProposedTaskStatus.Approved),
            RejectedTaskCount = t.Tasks.Count(x => x.Status == ProposedTaskStatus.Rejected),
            AccessLevel = t.AccessLevel.ToString(),
            Tasks = new()
        }).ToList();

        return new GetTranscriptListResponse
        {
            Items = dtos,
            TotalCount = totalCount,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize
        };
    }
}
