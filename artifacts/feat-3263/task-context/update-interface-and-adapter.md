### task: update-interface-and-adapter

**What and why:** Add XML `<exception>` doc tags to `IArticleUserResolver.ResolveByGroupAsync` so callers know which exceptions to expect. Then add a `try/catch` block inside `GraphArticleUserResolver.ResolveByGroupAsync` to catch the two infrastructure-specific exception types and re-throw them as the new Article-domain exceptions.

**File to modify (XML doc):**

`backend/src/Anela.Heblo.Application/Features/Article/Contracts/IArticleUserResolver.cs`

Replace the current interface method signature block with:

```csharp
namespace Anela.Heblo.Application.Features.Article.Contracts;

/// <summary>
/// Article-owned read-only abstraction for resolving the set of users associated
/// with a directory group (used by the RequestedBy backfill admin command).
/// Implemented by the UserManagement module via an adapter.
/// </summary>
public interface IArticleUserResolver
{
    /// <summary>
    /// Resolves the members of the given directory group.
    /// </summary>
    /// <param name="groupId">The group identifier to resolve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A read-only list of matched users.</returns>
    /// <exception cref="ArticleUserResolverAuthException">
    /// Thrown when token acquisition or authentication fails.
    /// </exception>
    /// <exception cref="ArticleUserResolverServiceException">
    /// Thrown when the remote directory service returns an error response.
    /// </exception>
    /// <exception cref="UnauthorizedAccessException">
    /// Thrown when the caller lacks permission to read the group.
    /// </exception>
    Task<IReadOnlyList<ArticleUserMatch>> ResolveByGroupAsync(
        string groupId,
        CancellationToken cancellationToken);
}

public sealed record ArticleUserMatch(string Id, string DisplayName);
```

**File to modify (try/catch):**

`backend/src/Anela.Heblo.Application/Features/UserManagement/Infrastructure/GraphArticleUserResolver.cs`

Replace the entire file content with:

```csharp
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
```

**Verify build:**

```
cd /home/user/worktrees/feature-3263-Arch-Review-Article-Backfillarticlerequestedbyhand/backend && dotnet build
```

Build must succeed with zero errors before committing.

**Commit:** `refactor(article): wrap infrastructure exceptions in GraphArticleUserResolver`

---
