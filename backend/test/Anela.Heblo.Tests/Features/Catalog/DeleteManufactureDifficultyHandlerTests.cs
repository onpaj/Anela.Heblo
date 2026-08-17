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
}
