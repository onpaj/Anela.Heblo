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
}
