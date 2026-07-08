using Anela.Heblo.Application.Shared.Rag;
using Anela.Heblo.Application.Shared.Users;
using Anela.Heblo.Domain.Features.Rag;
using MediatR;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.GetDraftReplyFeedbackList;

public class GetDraftReplyFeedbackListHandler
    : IRequestHandler<GetDraftReplyFeedbackListRequest, GetDraftReplyFeedbackListResponse>
{
    private static readonly int[] AllowedPageSizes = [10, 20, 50];
    private static readonly string[] AllowedSortColumns = ["CreatedAt", "PrecisionScore", "StyleScore"];

    private readonly IRagInteractionLogRepository _repository;
    private readonly IUserDisplayNameResolver _userDisplayNameResolver;

    public GetDraftReplyFeedbackListHandler(
        IRagInteractionLogRepository repository,
        IUserDisplayNameResolver userDisplayNameResolver)
    {
        _repository = repository;
        _userDisplayNameResolver = userDisplayNameResolver;
    }

    public async Task<GetDraftReplyFeedbackListResponse> Handle(
        GetDraftReplyFeedbackListRequest request,
        CancellationToken cancellationToken)
    {
        var pageNumber = Math.Max(1, request.PageNumber);
        var pageSize = AllowedPageSizes.Contains(request.PageSize) ? request.PageSize : 20;
        var sortBy = AllowedSortColumns.Contains(request.SortBy) ? request.SortBy : "CreatedAt";

        var (logs, totalCount) = await _repository.GetFeedbackLogsPagedAsync(
            RagFeature.SmartsuppDraftReply,
            request.HasFeedback,
            request.UserId,
            sortBy,
            request.SortDescending,
            pageNumber,
            pageSize,
            cancellationToken);

        var stats = await _repository.GetFeedbackStatsAsync(RagFeature.SmartsuppDraftReply, cancellationToken);

        var userNames = await _userDisplayNameResolver.ResolveAsync(
            logs.Select(l => l.UserId).Where(id => id is not null)!,
            cancellationToken);

        return new GetDraftReplyFeedbackListResponse
        {
            Logs = logs.Select(l => RagFeedbackListMapper.ToSummary(
                l,
                l.UserId is not null ? userNames.GetValueOrDefault(l.UserId) : null)).ToList(),
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize,
            Stats = RagFeedbackListMapper.ToStats(stats),
        };
    }
}
