namespace Anela.Heblo.Application.Features.Article.Contracts;

/// <summary>
/// Article-owned read-only abstraction for resolving the set of users associated
/// with a directory group (used by the RequestedBy backfill admin command).
/// Implemented by the UserManagement module via an adapter.
/// </summary>
public interface IArticleUserResolver
{
    Task<IReadOnlyList<ArticleUserMatch>> ResolveByGroupAsync(
        string groupId,
        CancellationToken cancellationToken);
}

public sealed record ArticleUserMatch(string Id, string DisplayName);
