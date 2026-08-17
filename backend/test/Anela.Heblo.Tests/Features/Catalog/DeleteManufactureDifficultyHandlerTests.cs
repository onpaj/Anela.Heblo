using Anela.Heblo.Application.Features.Catalog.UseCases.DeleteManufactureDifficulty;
using Anela.Heblo.Domain.Features.Catalog;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

public class DeleteManufactureDifficultyHandlerTests
{
    private readonly Mock<IManufactureDifficultyRepository> _repositoryMock;
    private readonly Mock<ICatalogRepository> _catalogRepositoryMock;
    private readonly Mock<ILogger<DeleteManufactureDifficultyHandler>> _loggerMock;
    private readonly DeleteManufactureDifficultyHandler _handler;

    public DeleteManufactureDifficultyHandlerTests()
    {
        _repositoryMock = new Mock<IManufactureDifficultyRepository>();
        _catalogRepositoryMock = new Mock<ICatalogRepository>();
        _loggerMock = new Mock<ILogger<DeleteManufactureDifficultyHandler>>();

        _handler = new DeleteManufactureDifficultyHandler(
            _repositoryMock.Object,
            _catalogRepositoryMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_NotFound_ReturnsFailureAndPerformsNoFurtherWork()
    {
        // Arrange
        var request = new DeleteManufactureDifficultyRequest { Id = 42 };

        _repositoryMock
            .Setup(r => r.GetByIdAsync(request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureDifficultySetting?)null);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.Message.Should().Be("ManufactureDifficultyHistory with ID 42 not found");

        _repositoryMock.Verify(
            r => r.DeleteAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _catalogRepositoryMock.Verify(
            r => r.RefreshManufactureDifficultySettingsData(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ExistingEntry_DeletesRefreshesCacheInOrderAndReturnsSuccess()
    {
        // Arrange
        var request = new DeleteManufactureDifficultyRequest { Id = 11 };
        var existing = new ManufactureDifficultySetting
        {
            Id = 11,
            ProductCode = "PROD-HAPPY",
            DifficultyValue = 2,
            ValidFrom = new DateTime(2024, 1, 1),
            ValidTo = new DateTime(2024, 12, 31)
        };

        _repositoryMock
            .Setup(r => r.GetByIdAsync(request.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var callSequence = new MockSequence();
        _repositoryMock
            .InSequence(callSequence)
            .Setup(r => r.DeleteAsync(request.Id, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _catalogRepositoryMock
            .InSequence(callSequence)
            .Setup(r => r.RefreshManufactureDifficultySettingsData(existing.ProductCode, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.Message.Should().Be("Manufacture difficulty deleted successfully");

        _repositoryMock.Verify(
            r => r.DeleteAsync(request.Id, It.IsAny<CancellationToken>()),
            Times.Once);

        // Crux of the original coverage gap: the cache refresh must receive the
        // deleted entity's ProductCode, not any value derived from the request.
        _catalogRepositoryMock.Verify(
            r => r.RefreshManufactureDifficultySettingsData(existing.ProductCode, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
