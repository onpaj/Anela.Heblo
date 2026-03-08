using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.DeleteDocument;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using Microsoft.Extensions.Logging;
using Moq;

namespace Anela.Heblo.Tests.KnowledgeBase.UseCases;

public class DeleteDocumentHandlerTests
{
    private readonly Mock<IKnowledgeBaseRepository> _repository = new();
    private readonly Mock<ILogger<DeleteDocumentHandler>> _logger = new();

    [Fact]
    public async Task Handle_CallsDeleteDocumentAsync_WithCorrectId()
    {
        var documentId = Guid.NewGuid();
        var request = new DeleteDocumentRequest { DocumentId = documentId };

        _repository
            .Setup(r => r.DeleteDocumentAsync(documentId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var handler = new DeleteDocumentHandler(_repository.Object, _logger.Object);

        await handler.Handle(request, CancellationToken.None);

        _repository.Verify(
            r => r.DeleteDocumentAsync(documentId, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
