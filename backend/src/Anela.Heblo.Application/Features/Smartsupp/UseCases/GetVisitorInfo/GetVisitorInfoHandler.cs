using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Smartsupp;
using MediatR;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.GetVisitorInfo;

public class GetVisitorInfoHandler : IRequestHandler<GetVisitorInfoRequest, GetVisitorInfoResponse>
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    private readonly ISmartsuppRepository _repo;
    private readonly ISmartsuppApiClient _apiClient;

    public GetVisitorInfoHandler(ISmartsuppRepository repo, ISmartsuppApiClient apiClient)
    {
        _repo = repo;
        _apiClient = apiClient;
    }

    public async Task<GetVisitorInfoResponse> Handle(
        GetVisitorInfoRequest request,
        CancellationToken cancellationToken)
    {
        var conversation = await _repo.GetConversationAsync(request.ConversationId, cancellationToken);
        if (conversation is null)
            return new GetVisitorInfoResponse(ErrorCodes.SmartsuppConversationNotFound);

        if (string.IsNullOrEmpty(conversation.VisitorId))
            return new GetVisitorInfoResponse(ErrorCodes.SmartsuppVisitorNotFound);

        var otherChats = conversation.ContactId is not null
            ? await _repo.ListConversationsForContactAsync(
                  conversation.ContactId, conversation.Id, cancellationToken)
            : [];
        var chatsCount = otherChats.Count + 1;

        var isCacheStale = conversation.VisitorInfoFetchedAt is null ||
                           DateTime.UtcNow - conversation.VisitorInfoFetchedAt.Value > CacheTtl;

        if (isCacheStale)
        {
            var visitor = await _apiClient.GetVisitorAsync(conversation.VisitorId, cancellationToken);

            var fetchedAt = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
            await _repo.UpdateVisitorCacheAsync(
                conversation.Id,
                visitor?.UserAgent,
                visitor?.Os,
                visitor?.Browser,
                visitor?.BrowserVersion,
                visitor?.VisitsCount,
                fetchedAt,
                cancellationToken);

            conversation.VisitorUserAgent = visitor?.UserAgent;
            conversation.VisitorOs = visitor?.Os;
            conversation.VisitorBrowser = visitor?.Browser;
            conversation.VisitorBrowserVersion = visitor?.BrowserVersion;
            conversation.VisitorVisitsCount = visitor?.VisitsCount;
        }

        var pages = BuildPageHistory(conversation);

        return new GetVisitorInfoResponse
        {
            VisitorInfo = new VisitorInfoDto
            {
                Os = conversation.VisitorOs,
                Browser = conversation.VisitorBrowser,
                BrowserVersion = conversation.VisitorBrowserVersion,
                UserAgent = conversation.VisitorUserAgent,
                VisitsCount = conversation.VisitorVisitsCount,
                ChatsCount = chatsCount,
                Pages = pages,
            }
        };
    }

    private static List<VisitorPageDto> BuildPageHistory(SmartsuppConversation conversation) =>
        conversation.Messages
            .Where(m => !string.IsNullOrEmpty(m.PageUrl))
            .GroupBy(m => m.PageUrl!)
            .Select(g => g.MinBy(m => m.CreatedAt)!)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new VisitorPageDto { Url = m.PageUrl! })
            .ToList();
}
