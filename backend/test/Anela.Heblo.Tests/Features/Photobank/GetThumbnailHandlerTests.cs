using Anela.Heblo.Application.Features.Photobank.Services;
using Anela.Heblo.Application.Features.Photobank.UseCases.GetThumbnail;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Photobank;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Photobank;

public sealed class GetThumbnailHandlerTests
{
    private readonly Mock<IPhotobankRepository> _repositoryMock = new();
    private readonly Mock<IPhotobankGraphService> _graphServiceMock = new();

    private GetThumbnailHandler CreateHandler() =>
        new(_repositoryMock.Object, _graphServiceMock.Object);

    private static GetThumbnailRequest Request(int id = 1, ThumbnailSize size = ThumbnailSize.Medium) =>
        new() { Id = id, Size = size };

    private void SetupLocator(PhotoLocator? locator, int id = 1) =>
        _repositoryMock
            .Setup(r => r.GetLocatorAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(locator);

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenLocatorMissing()
    {
        // Arrange
        SetupLocator(null);

        // Act
        var response = await CreateHandler().Handle(Request(), CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.PhotobankThumbnailNotFound);
        _graphServiceMock.Verify(
            g => g.GetThumbnailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ThumbnailSize>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ReturnsNotFound_WhenGraphReturnsNotFound()
    {
        // Arrange
        var locator = new PhotoLocator("driveId", "fileId", DateTime.UtcNow);
        SetupLocator(locator);
        _graphServiceMock
            .Setup(g => g.GetThumbnailAsync(locator.DriveId, locator.SharePointFileId, ThumbnailSize.Medium, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetThumbnailResult.NotFound());

        // Act
        var response = await CreateHandler().Handle(Request(), CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.PhotobankThumbnailNotFound);
    }

    [Fact]
    public async Task Handle_ReturnsThrottledWithRoundedRetryAfter_WhenGraphThrottles()
    {
        // Arrange
        var locator = new PhotoLocator("driveId", "fileId", DateTime.UtcNow);
        SetupLocator(locator);
        _graphServiceMock
            .Setup(g => g.GetThumbnailAsync(locator.DriveId, locator.SharePointFileId, ThumbnailSize.Medium, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetThumbnailResult.Throttled(TimeSpan.FromSeconds(29.3)));

        // Act
        var response = await CreateHandler().Handle(Request(), CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.PhotobankThumbnailThrottled);
        response.RetryAfterSeconds.Should().Be(30);
    }

    [Fact]
    public async Task Handle_ReturnsThrottledWithoutRetryAfter_WhenRetryAfterNull()
    {
        // Arrange
        var locator = new PhotoLocator("driveId", "fileId", DateTime.UtcNow);
        SetupLocator(locator);
        _graphServiceMock
            .Setup(g => g.GetThumbnailAsync(locator.DriveId, locator.SharePointFileId, ThumbnailSize.Medium, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetThumbnailResult.Throttled(null));

        // Act
        var response = await CreateHandler().Handle(Request(), CancellationToken.None);

        // Assert
        response.ErrorCode.Should().Be(ErrorCodes.PhotobankThumbnailThrottled);
        response.RetryAfterSeconds.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ReturnsUpstream_WhenGraphReturnsUpstreamError()
    {
        // Arrange
        var locator = new PhotoLocator("driveId", "fileId", DateTime.UtcNow);
        SetupLocator(locator);
        _graphServiceMock
            .Setup(g => g.GetThumbnailAsync(locator.DriveId, locator.SharePointFileId, ThumbnailSize.Medium, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetThumbnailResult.UpstreamError(new Exception("upstream error")));

        // Act
        var response = await CreateHandler().Handle(Request(), CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.PhotobankThumbnailUpstream);
    }

    [Fact]
    public async Task Handle_ReturnsAuthUnavailable_WhenGraphReturnsAuthUnavailable()
    {
        // Arrange
        var locator = new PhotoLocator("driveId", "fileId", DateTime.UtcNow);
        SetupLocator(locator);
        _graphServiceMock
            .Setup(g => g.GetThumbnailAsync(locator.DriveId, locator.SharePointFileId, ThumbnailSize.Medium, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetThumbnailResult.AuthUnavailable(new Exception("auth failure")));

        // Act
        var response = await CreateHandler().Handle(Request(), CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.PhotobankThumbnailAuthUnavailable);
    }

    [Fact]
    public async Task Handle_ReturnsSuccessWithSameStream_WhenThumbnailReturned()
    {
        // Arrange
        var locator = new PhotoLocator("driveId", "fileId", DateTime.UtcNow);
        var stream = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 });
        var thumbnail = new GraphThumbnail(stream, "image/jpeg", 12345L);
        SetupLocator(locator);
        _graphServiceMock
            .Setup(g => g.GetThumbnailAsync(locator.DriveId, locator.SharePointFileId, ThumbnailSize.Medium, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetThumbnailResult.Success(thumbnail));

        // Act
        var response = await CreateHandler().Handle(Request(), CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.ErrorCode.Should().BeNull();
        response.Content.Should().BeSameAs(stream);
        response.ContentType.Should().Be("image/jpeg");
        response.ContentLength.Should().Be(12345L);
        response.Content!.CanRead.Should().BeTrue("the handler must not dispose the stream before the framework writes it");
    }

    [Fact]
    public async Task Handle_PassesCancellationTokenThrough()
    {
        // Arrange
        var locator = new PhotoLocator("driveId", "fileId", DateTime.UtcNow);
        using var cts = new CancellationTokenSource();
        _repositoryMock
            .Setup(r => r.GetLocatorAsync(1, cts.Token))
            .ReturnsAsync(locator);
        _graphServiceMock
            .Setup(g => g.GetThumbnailAsync(locator.DriveId, locator.SharePointFileId, ThumbnailSize.Medium, cts.Token))
            .ReturnsAsync(new GetThumbnailResult.NotFound());

        // Act
        await CreateHandler().Handle(Request(), cts.Token);

        // Assert
        _repositoryMock.Verify(r => r.GetLocatorAsync(1, cts.Token), Times.Once);
        _graphServiceMock.Verify(
            g => g.GetThumbnailAsync(locator.DriveId, locator.SharePointFileId, ThumbnailSize.Medium, cts.Token),
            Times.Once);
    }
}
