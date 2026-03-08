using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.DeleteDocument;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.UseCases;

public class DeleteDocumentHandlerTests
{
    private readonly Mock<IKnowledgeBaseRepository> _repository = new();

    [Fact]
    public async Task Handle_DeletesDocumentById()
    {
        var documentId = Guid.NewGuid();

        _repository
            .Setup(r => r.DeleteDocumentAsync(documentId, default))
            .Returns(Task.CompletedTask);

        var handler = new DeleteDocumentHandler(_repository.Object);
        var result = await handler.Handle(
            new DeleteDocumentRequest { DocumentId = documentId },
            default);

        Assert.True(result.Success);
        _repository.Verify(r => r.DeleteDocumentAsync(documentId, default), Times.Once);
    }
}
