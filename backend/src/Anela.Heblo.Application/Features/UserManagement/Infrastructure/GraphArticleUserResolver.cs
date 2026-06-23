using Anela.Heblo.Application.Features.Article.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Services;
using Microsoft.Identity.Client;

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
        try
        {
            var members = await _graph.GetGroupMembersAsync(groupId, cancellationToken);
            return members
                .Select(m => new ArticleUserMatch(m.Id, m.DisplayName))
                .ToList();
        }
        catch (MsalException ex)
        {
            throw new ArticleUserResolverAuthException(
                $"Token acquisition failed for group {groupId}.", ex);
        }
        catch (Microsoft.Graph.Models.ODataErrors.ODataError ex)
        {
            throw new ArticleUserResolverServiceException(
                $"Graph OData error for group {groupId}.", ex);
        }
        // UnauthorizedAccessException and Exception propagate as-is;
        // BackfillArticleRequestedByHandler already catches both.
    }
}
