using Anela.Heblo.Application.Features.Article.Contracts;
using Anela.Heblo.Application.Features.KnowledgeBase.Services;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Infrastructure;

internal sealed class KnowledgeBaseArticleStyleGuideSource : IArticleStyleGuideSource
{
    private readonly IOneDriveService _oneDrive;

    public KnowledgeBaseArticleStyleGuideSource(IOneDriveService oneDrive)
    {
        _oneDrive = oneDrive;
    }

    public Task<string> DownloadStyleGuideTextAsync(
        string driveId,
        string path,
        CancellationToken cancellationToken) =>
        _oneDrive.DownloadFileTextByPathAsync(driveId, path, cancellationToken);
}
