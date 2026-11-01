using System.Linq.Expressions;
using Anela.Heblo.Application.Features.Catalog.UseCases.SetNd;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Attributes;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

public class SetNdHandlerTests
{
    private const string ProductCode = "PROD001";

    private readonly Mock<ICatalogRepository> _catalogRepositoryMock = new();
    private readonly Mock<ICatalogNdWriter> _writerMock = new();
    private readonly Mock<ILogger<SetNdHandler>> _loggerMock = new();
    private readonly SetNdHandler _handler;

    public SetNdHandlerTests()
    {
        _handler = new SetNdHandler(_catalogRepositoryMock.Object, _writerMock.Object, _loggerMock.Object);
    }

    private void SetupProductExists(bool exists)
    {
        _catalogRepositoryMock
            .Setup(r => r.SingleOrDefaultAsync(It.IsAny<Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(exists ? new CatalogAggregate { ProductCode = ProductCode } : null);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Handle_ExistingProduct_WritesFlagThenPatchesCatalogCache(bool isNd)
    {
        // Arrange
        SetupProductExists(true);
        var callOrder = new List<string>();
        _writerMock
            .Setup(w => w.SetAsync(ProductCode, isNd, It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("write"))
            .Returns(Task.CompletedTask);
        _catalogRepositoryMock
            .Setup(r => r.ApplyNdFlagAsync(ProductCode, isNd))
            .Callback(() => callOrder.Add("cache"))
            .Returns(Task.CompletedTask);

        // Act
        var response = await _handler.Handle(
            new SetNdRequest { ProductCode = ProductCode, IsNd = isNd },
            CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.ProductCode.Should().Be(ProductCode);
        response.IsNd.Should().Be(isNd);
        callOrder.Should().Equal("write", "cache");
    }

    [Fact]
    public async Task Handle_UnknownProduct_ReturnsProductNotFoundWithoutTouchingErp()
    {
        // Arrange
        SetupProductExists(false);

        // Act
        var response = await _handler.Handle(
            new SetNdRequest { ProductCode = ProductCode, IsNd = true },
            CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ProductNotFound);
        response.Params.Should().Contain("productCode", ProductCode);
        _writerMock.VerifyNoOtherCalls();
        _catalogRepositoryMock.Verify(r => r.ApplyNdFlagAsync(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenErpWriteFails_ReturnsNdWriteFailedAndSkipsCachePatch()
    {
        // Arrange
        SetupProductExists(true);
        _writerMock
            .Setup(w => w.SetAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Flexi said no"));

        // Act
        var response = await _handler.Handle(
            new SetNdRequest { ProductCode = ProductCode, IsNd = true },
            CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.NdWriteFailed);
        response.Params.Should().Contain("productCode", ProductCode);
        _catalogRepositoryMock.Verify(r => r.ApplyNdFlagAsync(It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenCachePatchFailsAfterSuccessfulWrite_StillReportsSuccess()
    {
        // Arrange - the flag is already in Flexi; the hourly refresh will catch up with it
        SetupProductExists(true);
        _writerMock
            .Setup(w => w.SetAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _catalogRepositoryMock
            .Setup(r => r.ApplyNdFlagAsync(It.IsAny<string>(), It.IsAny<bool>()))
            .ThrowsAsync(new InvalidOperationException("cache unavailable"));

        // Act
        var response = await _handler.Handle(
            new SetNdRequest { ProductCode = ProductCode, IsNd = true },
            CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.IsNd.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_WhenRequestIsCancelled_PropagatesCancellationInsteadOfMappingToError()
    {
        // Arrange
        SetupProductExists(true);
        using var cts = new CancellationTokenSource();
        _writerMock
            .Setup(w => w.SetAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback(() => cts.Cancel())
            .ThrowsAsync(new TaskCanceledException());

        // Act
        var act = () => _handler.Handle(
            new SetNdRequest { ProductCode = ProductCode, IsNd = true },
            cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
