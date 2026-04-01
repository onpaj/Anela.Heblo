using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.IndexDocument;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.UseCases;

public class IndexDocumentHandlerTests
{
    private readonly Mock<IKnowledgeBaseRepository> _repository = new();
    private readonly Mock<IDocumentIndexingService> _indexingService = new();

    private IndexDocumentHandler CreateHandler() =>
        new(_repository.Object, _indexingService.Object, NullLogger<IndexDocumentHandler>.Instance);

    [Fact]
    public async Task Handle_StoresDocumentAndChunksWithEmbeddings()
    {
        var pdfBytes = new byte[] { 1, 2, 3 };

        KnowledgeBaseDocument? savedDoc = null;
        _repository.Setup(r => r.AddDocumentAsync(It.IsAny<KnowledgeBaseDocument>(), default))
            .Callback<KnowledgeBaseDocument, CancellationToken>((doc, _) => savedDoc = doc);

        await CreateHandler().Handle(new IndexDocumentRequest
        {
            Filename = "test.pdf",
            SourcePath = "/inbox/test.pdf",
            ContentType = "application/pdf",
            Content = pdfBytes,
            ContentHash = "abc123def456"
        }, default);

        Assert.NotNull(savedDoc);
        Assert.Equal("test.pdf", savedDoc!.Filename);
        Assert.Equal("abc123def456", savedDoc.ContentHash);
        _indexingService.Verify(s => s.IndexChunksAsync(pdfBytes, "application/pdf", savedDoc, default), Times.Once);
        _repository.Verify(r => r.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task Handle_ThrowsForUnsupportedContentType()
    {
        _indexingService
            .Setup(s => s.IndexChunksAsync(It.IsAny<byte[]>(), "image/png", It.IsAny<KnowledgeBaseDocument>(), default))
            .ThrowsAsync(new NotSupportedException());

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            CreateHandler().Handle(new IndexDocumentRequest
            {
                Filename = "photo.png",
                SourcePath = "/inbox/photo.png",
                ContentType = "image/png",
                Content = [1, 2, 3],
                ContentHash = "abc"
            }, default));
    }

    [Fact]
    public async Task Handle_ThrowsNotSupportedException_WhenNoExtractorMatchesContentType()
    {
        var request = new IndexDocumentRequest
        {
            Filename = "file.xyz",
            SourcePath = "/file.xyz",
            ContentType = "application/unknown",
            Content = [1, 2, 3],
            ContentHash = "abc123",
        };

        _indexingService
            .Setup(s => s.IndexChunksAsync(It.IsAny<byte[]>(), "application/unknown", It.IsAny<KnowledgeBaseDocument>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotSupportedException());

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            CreateHandler().Handle(request, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_SetsDocumentTypeFromRequest()
    {
        KnowledgeBaseDocument? savedDoc = null;
        _repository.Setup(r => r.AddDocumentAsync(It.IsAny<KnowledgeBaseDocument>(), default))
            .Callback<KnowledgeBaseDocument, CancellationToken>((doc, _) => savedDoc = doc);

        await CreateHandler().Handle(new IndexDocumentRequest
        {
            Filename = "chat.txt",
            SourcePath = "/Conversation/Inbox/chat.txt",
            ContentType = "text/plain",
            Content = [1, 2, 3],
            ContentHash = "abc123",
            DocumentType = DocumentType.Conversation
        }, default);

        Assert.NotNull(savedDoc);
        Assert.Equal(DocumentType.Conversation, savedDoc!.DocumentType);
    }
}
