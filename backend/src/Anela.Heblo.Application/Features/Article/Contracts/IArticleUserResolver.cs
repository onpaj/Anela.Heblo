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
