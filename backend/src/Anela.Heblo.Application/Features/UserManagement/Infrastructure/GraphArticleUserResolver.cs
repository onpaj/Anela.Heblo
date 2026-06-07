using Anela.Heblo.Application.Features.Article.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Services;

namespace Anela.Heblo.Application.Features.UserManagement.Infrastructure;

internal sealed class GraphArticleUserResolver : IArticleUserResolver
{
    private readonly IGraphService _graph;

    public GraphArticleUserResolver(IGraphService graph)
    {
        _graph = graph;
    }

    public async Task<IReadOnlyList<ArticleUserMatch>> ResolveByGroupAsync(
        string groupId,
        CancellationToken cancellationToken)
    {
        var members = await _graph.GetGroupMembersAsync(groupId, cancellationToken);
        return members
            .Select(m => new ArticleUserMatch(m.Id, m.DisplayName))
            .ToList();
    }
}
