namespace Anela.Heblo.Application.Features.Article.Contracts;

/// <summary>
/// Retrieves the Article module's style guide text from an external source.
/// Implemented by the KnowledgeBase module via an adapter.
/// </summary>
public interface IArticleStyleGuideSource
{
    Task<string> DownloadStyleGuideTextAsync(
        string driveId,
        string path,
        CancellationToken cancellationToken);
}
