namespace Anela.Heblo.Application.Features.Article.Contracts;

/// <summary>
/// Thrown by <see cref="IArticleUserResolver"/> implementations when the remote
/// directory service returns an error response (e.g. an OData error from Microsoft Graph).
/// Wraps infrastructure-specific service exceptions so that Application-layer consumers
/// remain decoupled from SDK packages.
/// </summary>
public sealed class ArticleUserResolverServiceException : Exception
{
    public ArticleUserResolverServiceException(string message, Exception innerException)
        : base(message, innerException) { }
}
