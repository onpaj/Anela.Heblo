using Anela.Heblo.Application.Features.Article.Contracts;
using Anela.Heblo.Application.Features.KnowledgeBase.Infrastructure;
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.KnowledgeBase.Infrastructure;

public class KnowledgeBaseArticleStyleGuideSourceTests
{
    private readonly Mock<IOneDriveService> _oneDrive = new();

    private IArticleStyleGuideSource CreateSut() =>
        new KnowledgeBaseArticleStyleGuideSource(_oneDrive.Object);

    [Fact]
    public async Task DownloadStyleGuideTextAsync_ForwardsArgumentsAndReturnsResult()
    {
        const string driveId = "drive-abc";
        const string path = "/style-guides/article.md";
        const string expected = "guide body";

        _oneDrive
            .Setup(o => o.DownloadFileTextByPathAsync(driveId, path, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var sut = CreateSut();

        var actual = await sut.DownloadStyleGuideTextAsync(driveId, path, CancellationToken.None);

        actual.Should().Be(expected);
        _oneDrive.Verify(
            o => o.DownloadFileTextByPathAsync(driveId, path, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DownloadStyleGuideTextAsync_PropagatesUnderlyingException()
    {
        _oneDrive
            .Setup(o => o.DownloadFileTextByPathAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("OneDrive down"));

        var sut = CreateSut();

        var act = () => sut.DownloadStyleGuideTextAsync("drive", "path", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("OneDrive down");
    }

    [Fact]
    public async Task DownloadStyleGuideTextAsync_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        _oneDrive
            .Setup(o => o.DownloadFileTextByPathAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var sut = CreateSut();

        var act = () => sut.DownloadStyleGuideTextAsync("drive", "path", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
