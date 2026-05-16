using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.IndexDocument;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using Anela.Heblo.Domain.Shared.Rag;
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

    [Fact]
    public async Task Handle_GraphItemId_Provided_And_DocExists_ReusesExistingDocument()
    {
        // Arrange
        var existingId = Guid.NewGuid();
        var existing = new KnowledgeBaseDocument
        {
            Id = existingId,
            Filename = "kb.pdf",
            SourcePath = "https://inbox.example.com/kb.pdf",
            ContentType = "application/pdf",
            ContentHash = "oldhash",
            DriveId = "drive-kb",
            GraphItemId = "item-kb",
            Status = DocumentStatus.Indexed,
            CreatedAt = DateTime.UtcNow,
        };

        _repository.Setup(r => r.GetDocumentByHashAsync(It.IsAny<string>(), default))
            .ReturnsAsync((KnowledgeBaseDocument?)null);
        _repository.Setup(r => r.GetDocumentByGraphItemIdAsync("drive-kb", "item-kb", default))
            .ReturnsAsync(existing);

        // Act
        var response = await CreateHandler().Handle(new IndexDocumentRequest
        {
            Filename = "kb.pdf",
            SourcePath = "https://inbox.example.com/kb.pdf",
            ContentType = "application/pdf",
            Content = [1, 2, 3],
            DriveId = "drive-kb",
            GraphItemId = "item-kb",
        }, default);

        // Assert: existing doc replaced (delete + re-add), GetBySourcePath never called
        _repository.Verify(r => r.GetDocumentByGraphItemIdAsync("drive-kb", "item-kb", default), Times.Once);
        _repository.Verify(r => r.GetDocumentBySourcePathAsync(It.IsAny<string>(), default), Times.Never);
        _repository.Verify(r => r.DeleteDocumentAsync(existingId, default), Times.Once);
        _repository.Verify(r => r.AddDocumentAsync(It.IsAny<KnowledgeBaseDocument>(), default), Times.Once);
        Assert.NotEqual(Guid.Empty, response.DocumentId);
        Assert.False(response.WasDuplicate);
    }

    [Fact]
    public async Task Handle_GraphItemId_Provided_And_DocDoesNotExist_CreatesNewDocWithDriveIdAndGraphItemId()
    {
        // Arrange
        _repository.Setup(r => r.GetDocumentByHashAsync(It.IsAny<string>(), default))
            .ReturnsAsync((KnowledgeBaseDocument?)null);
        _repository.Setup(r => r.GetDocumentByGraphItemIdAsync("drive-new", "item-new", default))
            .ReturnsAsync((KnowledgeBaseDocument?)null);

        KnowledgeBaseDocument? savedDoc = null;
        _repository.Setup(r => r.AddDocumentAsync(It.IsAny<KnowledgeBaseDocument>(), default))
            .Callback<KnowledgeBaseDocument, CancellationToken>((doc, _) => savedDoc = doc);

        // Act
        await CreateHandler().Handle(new IndexDocumentRequest
        {
            Filename = "guide.pdf",
            SourcePath = "https://onedrive.example.com/guide.pdf",
            ContentType = "application/pdf",
            Content = [10, 20, 30],
            DriveId = "drive-new",
            GraphItemId = "item-new",
        }, default);

        // Assert
        _repository.Verify(r => r.GetDocumentByGraphItemIdAsync("drive-new", "item-new", default), Times.Once);
        _repository.Verify(r => r.GetDocumentBySourcePathAsync(It.IsAny<string>(), default), Times.Never);

        Assert.NotNull(savedDoc);
        Assert.Equal("drive-new", savedDoc!.DriveId);
        Assert.Equal("item-new", savedDoc.GraphItemId);
    }

    [Fact]
    public async Task Handle_GraphItemId_Null_FallsBackToGetBySourcePath_UploadFlow()
    {
        // Arrange
        _repository.Setup(r => r.GetDocumentByHashAsync(It.IsAny<string>(), default))
            .ReturnsAsync((KnowledgeBaseDocument?)null);
        _repository.Setup(r => r.GetDocumentBySourcePathAsync(It.IsAny<string>(), default))
            .ReturnsAsync((KnowledgeBaseDocument?)null);

        // Act
        await CreateHandler().Handle(new IndexDocumentRequest
        {
            Filename = "upload.pdf",
            SourcePath = "upload/some-guid/upload.pdf",
            ContentType = "application/pdf",
            Content = [5, 6, 7],
            // DriveId and GraphItemId intentionally omitted (upload flow)
        }, default);

        // Assert: upload flow uses GetDocumentBySourcePathAsync, never GetDocumentByGraphItemIdAsync
        _repository.Verify(r => r.GetDocumentBySourcePathAsync("upload/some-guid/upload.pdf", default), Times.Once);
        _repository.Verify(r => r.GetDocumentByGraphItemIdAsync(It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_GraphItemId_Provided_But_DriveId_Null_FallsBackToGetBySourcePath()
    {
        // Arrange
        _repository.Setup(r => r.GetDocumentByHashAsync(It.IsAny<string>(), default))
            .ReturnsAsync((KnowledgeBaseDocument?)null);
        _repository.Setup(r => r.GetDocumentBySourcePathAsync(It.IsAny<string>(), default))
            .ReturnsAsync((KnowledgeBaseDocument?)null);

        // Act
        await CreateHandler().Handle(new IndexDocumentRequest
        {
            Filename = "partial.pdf",
            SourcePath = "/inbox/partial.pdf",
            ContentType = "application/pdf",
            Content = [1, 2, 3],
            GraphItemId = "item-abc",
            // DriveId intentionally omitted — should not crash or call GetDocumentByGraphItemIdAsync
        }, default);

        // Assert: falls back to source path, GraphItemId lookup never called
        _repository.Verify(r => r.GetDocumentBySourcePathAsync("/inbox/partial.pdf", default), Times.Once);
        _repository.Verify(r => r.GetDocumentByGraphItemIdAsync(It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task Handle_HashMatch_LegacyDoc_WithGraphIdentity_BackfillsGraphItemId()
    {
        // Arrange
        var content = new byte[] { 10, 20, 30 };
        var contentHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content));
        var existingId = Guid.NewGuid();
        var legacyDoc = new KnowledgeBaseDocument
        {
            Id = existingId,
            Filename = "old.pdf",
            SourcePath = "/inbox/old.pdf",
            ContentType = "application/pdf",
            ContentHash = contentHash,
            Status = DocumentStatus.Indexed,
            CreatedAt = DateTime.UtcNow,
            // DriveId/GraphItemId null — legacy row
        };

        _repository.Setup(r => r.GetDocumentByHashAsync(contentHash, default))
            .ReturnsAsync(legacyDoc);

        // Act
        var response = await CreateHandler().Handle(new IndexDocumentRequest
        {
            Filename = "old.pdf",
            SourcePath = "/inbox/old.pdf",
            ContentType = "application/pdf",
            Content = content,
            DriveId = "drive-backfill",
            GraphItemId = "item-backfill",
        }, default);

        // Assert: returned as duplicate and GraphItemId backfilled
        Assert.True(response.WasDuplicate);
        Assert.Equal(existingId, response.DocumentId);
        _repository.Verify(
            r => r.UpdateDocumentGraphItemIdAsync(existingId, "drive-backfill", "item-backfill", default),
            Times.Once);
    }

    [Fact]
    public async Task Handle_HashMatch_DocAlreadyHasGraphItemId_DoesNotBackfill()
    {
        // Arrange
        var content = new byte[] { 10, 20, 30 };
        var contentHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content));
        var existingId = Guid.NewGuid();
        var existingDoc = new KnowledgeBaseDocument
        {
            Id = existingId,
            Filename = "already.pdf",
            SourcePath = "/inbox/already.pdf",
            ContentType = "application/pdf",
            ContentHash = contentHash,
            Status = DocumentStatus.Indexed,
            CreatedAt = DateTime.UtcNow,
            DriveId = "drive-existing",
            GraphItemId = "item-existing",
        };

        _repository.Setup(r => r.GetDocumentByHashAsync(contentHash, default))
            .ReturnsAsync(existingDoc);

        // Act
        var response = await CreateHandler().Handle(new IndexDocumentRequest
        {
            Filename = "already.pdf",
            SourcePath = "/inbox/already.pdf",
            ContentType = "application/pdf",
            Content = content,
            DriveId = "drive-existing",
            GraphItemId = "item-existing",
        }, default);

        // Assert: no backfill call when GraphItemId already set
        Assert.True(response.WasDuplicate);
        _repository.Verify(
            r => r.UpdateDocumentGraphItemIdAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), default),
            Times.Never);
    }
}
