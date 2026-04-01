using Anela.Heblo.API.Features.ExpeditionList;
using Anela.Heblo.Application.Features.ExpeditionList.Services;
using Moq;

namespace Anela.Heblo.Tests.Features.ExpeditionList;

public class CombinedPrintQueueSinkTests
{
    private readonly Mock<IPrintQueueSink> _azureSink = new();
    private readonly Mock<IPrintQueueSink> _cupsSink = new();

    private CombinedPrintQueueSink CreateSink() =>
        new CombinedPrintQueueSink(_azureSink.Object, _cupsSink.Object);

    [Fact]
    public async Task SendAsync_BothSucceed_CallsBothSinksWithSamePaths()
    {
        // Arrange
        var files = new List<string> { "/tmp/a.pdf", "/tmp/b.pdf" };
        _azureSink
            .Setup(x => x.SendAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _cupsSink
            .Setup(x => x.SendAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sink = CreateSink();

        // Act
        await sink.SendAsync(files);

        // Assert
        _azureSink.Verify(
            x => x.SendAsync(
                It.Is<IEnumerable<string>>(p => p.SequenceEqual(files)),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _cupsSink.Verify(
            x => x.SendAsync(
                It.Is<IEnumerable<string>>(p => p.SequenceEqual(files)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SendAsync_AzureThrows_CupsNeverCalledAndExceptionPropagates()
    {
        // Arrange
        var files = new List<string> { "/tmp/a.pdf" };
        _azureSink
            .Setup(x => x.SendAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("azure failed"));

        var sink = CreateSink();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => sink.SendAsync(files));
        _cupsSink.Verify(
            x => x.SendAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SendAsync_AzureSucceedsCupsThrows_ExceptionPropagates()
    {
        // Arrange
        var files = new List<string> { "/tmp/a.pdf" };
        _azureSink
            .Setup(x => x.SendAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _cupsSink
            .Setup(x => x.SendAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("cups failed"));

        var sink = CreateSink();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => sink.SendAsync(files));
    }

    [Fact]
    public async Task SendAsync_SinglePassEnumerable_BothSinksReceiveAllPaths()
    {
        // Arrange: yield-return produces a single-pass IEnumerable
        IEnumerable<string> SinglePass()
        {
            yield return "/tmp/a.pdf";
            yield return "/tmp/b.pdf";
        }

        List<string>? azureCaptured = null;
        List<string>? cupsCaptured = null;

        _azureSink
            .Setup(x => x.SendAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<string>, CancellationToken>((paths, _) => azureCaptured = paths.ToList())
            .Returns(Task.CompletedTask);
        _cupsSink
            .Setup(x => x.SendAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<string>, CancellationToken>((paths, _) => cupsCaptured = paths.ToList())
            .Returns(Task.CompletedTask);

        var sink = CreateSink();

        // Act
        await sink.SendAsync(SinglePass());

        // Assert: both sinks got both paths, not an empty sequence
        Assert.Equal(["/tmp/a.pdf", "/tmp/b.pdf"], azureCaptured);
        Assert.Equal(["/tmp/a.pdf", "/tmp/b.pdf"], cupsCaptured);
    }
}
