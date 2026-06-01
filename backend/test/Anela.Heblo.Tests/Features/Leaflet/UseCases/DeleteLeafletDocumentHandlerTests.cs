using Anela.Heblo.Application.Features.Leaflet.UseCases.DeleteLeafletDocument;
using Anela.Heblo.Domain.Features.Leaflet;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Leaflet.UseCases;

public class DeleteLeafletDocumentHandlerTests
{
    private readonly Mock<ILeafletDocumentRepository> _repoMock = new();

    private DeleteLeafletDocumentHandler CreateHandler() =>
        new(_repoMock.Object);

    [Fact]
    public async Task Handle_calls_repository_delete_and_returns_success()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        _repoMock
            .Setup(r => r.DeleteDocumentAsync(documentId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = CreateHandler();
        var request = new DeleteLeafletDocumentRequest { DocumentId = documentId };

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        _repoMock.Verify(r => r.DeleteDocumentAsync(documentId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_propagates_exception_when_repository_throws()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        _repoMock
            .Setup(r => r.DeleteDocumentAsync(documentId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Document not found"));

        var handler = CreateHandler();
        var request = new DeleteLeafletDocumentRequest { DocumentId = documentId };

        // Act
        var act = () => handler.Handle(request, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Document not found");
    }
}
