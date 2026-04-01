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
        }, default);

        var expectedHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pdfBytes));
        Assert.NotNull(savedDoc);
        Assert.Equal("test.pdf", savedDoc!.Filename);
        Assert.Equal(expectedHash, savedDoc.ContentHash);
        _indexingService.Verify(s => s.IndexChunksAsync(pdfBytes, "application/pdf", savedDoc, default), Times.Once);
        _repository.Verify(r => r.SaveChangesAsync(default), Times.Exactly(2));
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
            DocumentType = DocumentType.Conversation
        }, default);

        Assert.NotNull(savedDoc);
        Assert.Equal(DocumentType.Conversation, savedDoc!.DocumentType);
    }

    [Fact]
    public async Task Handle_DuplicateByHash_SamePath_ReturnsWasDuplicateWithExistingId()
    {
        var content = new byte[] { 10, 20, 30 };
        var contentHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content));
        var existingId = Guid.NewGuid();
        var existing = new KnowledgeBaseDocument
        {
            Id = existingId,
            Filename = "doc.pdf",
            SourcePath = "/inbox/doc.pdf",
            ContentType = "application/pdf",
            ContentHash = contentHash,
            Status = DocumentStatus.Indexed,
            CreatedAt = DateTime.UtcNow,
        };

        _repository.Setup(r => r.GetDocumentByHashAsync(contentHash, default))
            .ReturnsAsync(existing);

        var response = await CreateHandler().Handle(new IndexDocumentRequest
        {
            Filename = "doc.pdf",
            SourcePath = "/inbox/doc.pdf",
            ContentType = "application/pdf",
            Content = content,
        }, default);

        Assert.True(response.WasDuplicate);
        Assert.Equal(existingId, response.DocumentId);
        _repository.Verify(r => r.AddDocumentAsync(It.IsAny<KnowledgeBaseDocument>(), default), Times.Never);
        _indexingService.Verify(s => s.IndexChunksAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<KnowledgeBaseDocument>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_DuplicateByHash_DifferentPath_UpdatesSourcePath()
    {
        var content = new byte[] { 10, 20, 30 };
        var contentHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content));
        var existingId = Guid.NewGuid();
        var existing = new KnowledgeBaseDocument
        {
            Id = existingId,
            Filename = "doc.pdf",
            SourcePath = "/old/path/doc.pdf",
            ContentType = "application/pdf",
            ContentHash = contentHash,
            Status = DocumentStatus.Indexed,
            CreatedAt = DateTime.UtcNow,
        };

        _repository.Setup(r => r.GetDocumentByHashAsync(contentHash, default))
            .ReturnsAsync(existing);

        var response = await CreateHandler().Handle(new IndexDocumentRequest
        {
            Filename = "doc.pdf",
            SourcePath = "/new/path/doc.pdf",
            ContentType = "application/pdf",
            Content = content,
        }, default);

        Assert.True(response.WasDuplicate);
        _repository.Verify(r => r.UpdateDocumentSourcePathAsync(existingId, "/new/path/doc.pdf", default), Times.Once);
        _repository.Verify(r => r.AddDocumentAsync(It.IsAny<KnowledgeBaseDocument>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_DuplicateByPath_DeletesOldAndReIndexes()
    {
        var content = new byte[] { 40, 50, 60 };
        var contentHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content));
        var oldDocumentId = Guid.NewGuid();
        var existingByPath = new KnowledgeBaseDocument
        {
            Id = oldDocumentId,
            Filename = "report.pdf",
            SourcePath = "/inbox/report.pdf",
            ContentType = "application/pdf",
            ContentHash = "oldhash",
            Status = DocumentStatus.Indexed,
            CreatedAt = DateTime.UtcNow,
        };

        _repository.Setup(r => r.GetDocumentByHashAsync(contentHash, default))
            .ReturnsAsync((KnowledgeBaseDocument?)null);
        _repository.Setup(r => r.GetDocumentBySourcePathAsync("/inbox/report.pdf", default))
            .ReturnsAsync(existingByPath);

        await CreateHandler().Handle(new IndexDocumentRequest
        {
            Filename = "report.pdf",
            SourcePath = "/inbox/report.pdf",
            ContentType = "application/pdf",
            Content = content,
        }, default);

        _repository.Verify(r => r.DeleteDocumentAsync(oldDocumentId, default), Times.Once);
        _repository.Verify(r => r.AddDocumentAsync(It.IsAny<KnowledgeBaseDocument>(), default), Times.Once);
        _indexingService.Verify(s => s.IndexChunksAsync(content, "application/pdf", It.IsAny<KnowledgeBaseDocument>(), default), Times.Once);
    }

    [Fact]
    public async Task Handle_IndexChunksThrows_SetsStatusToFailedAndRethrows()
    {
        var content = new byte[] { 7, 8, 9 };

        _repository.Setup(r => r.GetDocumentByHashAsync(It.IsAny<string>(), default))
            .ReturnsAsync((KnowledgeBaseDocument?)null);
        _repository.Setup(r => r.GetDocumentBySourcePathAsync(It.IsAny<string>(), default))
            .ReturnsAsync((KnowledgeBaseDocument?)null);

        KnowledgeBaseDocument? savedDoc = null;
        _repository.Setup(r => r.AddDocumentAsync(It.IsAny<KnowledgeBaseDocument>(), default))
            .Callback<KnowledgeBaseDocument, CancellationToken>((doc, _) => savedDoc = doc);

        _indexingService
            .Setup(s => s.IndexChunksAsync(content, "application/pdf", It.IsAny<KnowledgeBaseDocument>(), default))
            .ThrowsAsync(new InvalidOperationException("embedding service unavailable"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateHandler().Handle(new IndexDocumentRequest
            {
                Filename = "fail.pdf",
                SourcePath = "/inbox/fail.pdf",
                ContentType = "application/pdf",
                Content = content,
            }, default));

        Assert.NotNull(savedDoc);
        Assert.Equal(DocumentStatus.Failed, savedDoc!.Status);
        // SaveChangesAsync is called once after AddDocumentAsync, and once after setting Failed status
        _repository.Verify(r => r.SaveChangesAsync(default), Times.AtLeast(2));
    }

    [Fact]
    public async Task Handle_OctetStream_ResolvesContentTypeFromExtension()
    {
        KnowledgeBaseDocument? savedDoc = null;
        _repository.Setup(r => r.AddDocumentAsync(It.IsAny<KnowledgeBaseDocument>(), default))
            .Callback<KnowledgeBaseDocument, CancellationToken>((doc, _) => savedDoc = doc);

        await CreateHandler().Handle(new IndexDocumentRequest
        {
            Filename = "readme.txt",
            SourcePath = "/inbox/readme.txt",
            ContentType = "application/octet-stream",
            Content = [1, 2, 3],
        }, default);

        Assert.NotNull(savedDoc);
        Assert.Equal("text/plain", savedDoc!.ContentType);
    }
}
