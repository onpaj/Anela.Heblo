using Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletDocumentContentTypes;
using Anela.Heblo.Domain.Features.Leaflet;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Leaflet.UseCases;

public class GetLeafletDocumentContentTypesHandlerTests
{
    private readonly Mock<ILeafletDocumentRepository> _repoMock = new();

    private GetLeafletDocumentContentTypesHandler CreateHandler() =>
        new(_repoMock.Object);

    [Fact]
    public async Task Handle_returns_distinct_content_types_from_repo()
    {
        // Arrange
        var contentTypes = new List<string> { "application/pdf", "text/plain", "text/markdown" };
        _repoMock
            .Setup(r => r.GetDistinctContentTypesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(contentTypes);

        var handler = CreateHandler();

        // Act
        var response = await handler.Handle(new GetLeafletDocumentContentTypesRequest(), CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.ContentTypes.Should().BeEquivalentTo(contentTypes);
    }

    [Fact]
    public async Task Handle_returns_empty_list_when_no_documents_exist()
    {
        // Arrange
        _repoMock
            .Setup(r => r.GetDistinctContentTypesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var handler = CreateHandler();

        // Act
        var response = await handler.Handle(new GetLeafletDocumentContentTypesRequest(), CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.ContentTypes.Should().BeEmpty();
    }
}
