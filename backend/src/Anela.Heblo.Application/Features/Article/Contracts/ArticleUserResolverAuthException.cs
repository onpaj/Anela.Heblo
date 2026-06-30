namespace Anela.Heblo.Application.Features.Article.Contracts;

/// <summary>
/// Thrown by <see cref="IArticleUserResolver"/> implementations when token acquisition
/// or authentication for the underlying identity provider fails.
/// Wraps infrastructure-specific auth exceptions (e.g. MsalException) so that
/// Application-layer consumers remain decoupled from SDK packages.
/// </summary>
public sealed class ArticleUserResolverAuthException : Exception
{
    public ArticleUserResolverAuthException(string message, Exception innerException)
        : base(message, innerException) { }
}
