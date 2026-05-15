using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Application.Features.Leaflet.Services;
using Anela.Heblo.Application.Features.Leaflet.UseCases.IndexLeaflet;
using Anela.Heblo.Domain.Features.Leaflet;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Anela.Heblo.Tests.Features.Leaflet.UseCases;

public class IndexLeafletHandlerTests
{
    private readonly Mock<ILeafletDocumentRepository> _repoMock;
    private readonly Mock<ILeafletIndexingService> _indexingMock;
    private readonly Mock<IDocumentTextExtractor> _extractorMock;

    public IndexLeafletHandlerTests()
    {
        _repoMock = new Mock<ILeafletDocumentRepository>();
        _indexingMock = new Mock<ILeafletIndexingService>();
        _extractorMock = new Mock<IDocumentTextExtractor>();

        _indexingMock
            .Setup(s => s.IndexAsync(It.IsAny<string>(), It.IsAny<LeafletDocument>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
    }

    private IndexLeafletHandler CreateHandler(IEnumerable<IDocumentTextExtractor>? extractors = null)
    {
        return new IndexLeafletHandler(
            _repoMock.Object,
            extractors ?? new[] { _extractorMock.Object },
            _indexingMock.Object,
            NullLogger<IndexLeafletHandler>.Instance);
    }

    [Fact]
    public async Task Handle_duplicate_hash_returns_WasDuplicate_true_and_skips_indexing()
    {
        // Arrange
        var existingId = Guid.NewGuid();
        var existingDoc = new LeafletDocument { Id = existingId, ContentHash = "abc" };

        _repoMock
            .Setup(r => r.GetByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingDoc);

        var request = new IndexLeafletRequest
        {
            Content = new byte[] { 1, 2, 3 },
            Filename = "file.txt",
            SourcePath = "/inbox/file.txt",
            ContentType = "text/plain",
        };

        var handler = CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.WasDuplicate.Should().BeTrue();
        response.DocumentId.Should().Be(existingId);

        _indexingMock.Verify(
            s => s.IndexAsync(It.IsAny<string>(), It.IsAny<LeafletDocument>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_path_collision_deletes_old_document_and_indexes_new()
    {
        // Arrange
        var oldId = Guid.NewGuid();
        var oldDoc = new LeafletDocument { Id = oldId, SourcePath = "/inbox/file.txt" };

        _repoMock
            .Setup(r => r.GetByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LeafletDocument?)null);
        _repoMock
            .Setup(r => r.GetBySourcePathAsync("/inbox/file.txt", It.IsAny<CancellationToken>()))
            .ReturnsAsync(oldDoc);

        _extractorMock.Setup(e => e.CanHandle("text/plain")).Returns(true);
        _extractorMock
            .Setup(e => e.ExtractTextAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("some extracted text");

        var request = new IndexLeafletRequest
        {
            Content = new byte[] { 10, 20, 30 },
            Filename = "file.txt",
            SourcePath = "/inbox/file.txt",
            ContentType = "text/plain",
        };

        var handler = CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        _repoMock.Verify(
            r => r.DeleteDocumentAsync(oldId, It.IsAny<CancellationToken>()),
            Times.Once);

        _indexingMock.Verify(
            s => s.IndexAsync(It.IsAny<string>(), It.IsAny<LeafletDocument>(), It.IsAny<CancellationToken>()),
            Times.Once);

        response.WasDuplicate.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_no_extractor_throws_NotSupportedException()
    {
        // Arrange
        _repoMock
            .Setup(r => r.GetByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LeafletDocument?)null);
        _repoMock
            .Setup(r => r.GetBySourcePathAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LeafletDocument?)null);

        var request = new IndexLeafletRequest
        {
            Content = new byte[] { 1, 2, 3 },
            Filename = "file.docx",
            SourcePath = "/inbox/file.docx",
            ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        };

        var handler = CreateHandler(extractors: Enumerable.Empty<IDocumentTextExtractor>());

        // Act
        var act = () => handler.Handle(request, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task Handle_happy_path_persists_and_calls_indexing()
    {
        // Arrange
        _repoMock
            .Setup(r => r.GetByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LeafletDocument?)null);
        _repoMock
            .Setup(r => r.GetBySourcePathAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LeafletDocument?)null);

        _extractorMock.Setup(e => e.CanHandle("application/pdf")).Returns(true);
        _extractorMock
            .Setup(e => e.ExtractTextAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("word1 word2 word3");

        var content = new byte[] { 100, 200, 255 };
        var request = new IndexLeafletRequest
        {
            Content = content,
            Filename = "brochure.pdf",
            SourcePath = "/inbox/brochure.pdf",
            ContentType = "application/pdf",
        };

        LeafletDocument? capturedDoc = null;
        _repoMock
            .Setup(r => r.AddDocumentAsync(It.IsAny<LeafletDocument>(), It.IsAny<CancellationToken>()))
            .Callback<LeafletDocument, CancellationToken>((doc, _) => capturedDoc = doc);

        var handler = CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.WasDuplicate.Should().BeFalse();
        response.DocumentId.Should().NotBeEmpty();

        capturedDoc.Should().NotBeNull();
        capturedDoc!.Filename.Should().Be("brochure.pdf");
        capturedDoc.SourcePath.Should().Be("/inbox/brochure.pdf");
        capturedDoc.ContentType.Should().Be("application/pdf");
        capturedDoc.ContentHash.Should().HaveLength(64);
        capturedDoc.ContentHash.Should().MatchRegex("^[0-9a-f]{64}$");

        _indexingMock.Verify(
            s => s.IndexAsync(It.IsAny<string>(), It.IsAny<LeafletDocument>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_graphItemId_provided_and_doc_exists_reuses_existing_document()
    {
        // Arrange
        var existingId = Guid.NewGuid();
        var existingDoc = new LeafletDocument
        {
            Id = existingId,
            ContentHash = "differenthash",
            DriveId = "drive-123",
            GraphItemId = "item-abc",
        };

        _repoMock
            .Setup(r => r.GetByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LeafletDocument?)null);
        _repoMock
            .Setup(r => r.GetByGraphItemIdAsync("drive-123", "item-abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingDoc);

        _extractorMock.Setup(e => e.CanHandle("application/pdf")).Returns(true);
        _extractorMock
            .Setup(e => e.ExtractTextAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("text");

        var request = new IndexLeafletRequest
        {
            Content = new byte[] { 1, 2, 3 },
            Filename = "file.pdf",
            SourcePath = "https://inbox.example.com/file.pdf",
            ContentType = "application/pdf",
            DriveId = "drive-123",
            GraphItemId = "item-abc",
        };

        var handler = CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert: existing doc reused, no new doc added
        _repoMock.Verify(
            r => r.GetByGraphItemIdAsync("drive-123", "item-abc", It.IsAny<CancellationToken>()),
            Times.Once);
        _repoMock.Verify(
            r => r.GetBySourcePathAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _repoMock.Verify(
            r => r.DeleteDocumentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _repoMock.Verify(
            r => r.AddDocumentAsync(It.IsAny<LeafletDocument>(), It.IsAny<CancellationToken>()),
            Times.Once);
        response.DocumentId.Should().NotBeEmpty();
        response.WasDuplicate.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_graphItemId_provided_and_doc_does_not_exist_creates_new_doc_with_driveId_and_graphItemId()
    {
        // Arrange
        _repoMock
            .Setup(r => r.GetByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LeafletDocument?)null);
        _repoMock
            .Setup(r => r.GetByGraphItemIdAsync("drive-456", "item-xyz", It.IsAny<CancellationToken>()))
            .ReturnsAsync((LeafletDocument?)null);

        _extractorMock.Setup(e => e.CanHandle("application/pdf")).Returns(true);
        _extractorMock
            .Setup(e => e.ExtractTextAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("extracted text");

        var request = new IndexLeafletRequest
        {
            Content = new byte[] { 10, 20, 30 },
            Filename = "catalog.pdf",
            SourcePath = "https://onedrive.example.com/catalog.pdf",
            ContentType = "application/pdf",
            DriveId = "drive-456",
            GraphItemId = "item-xyz",
        };

        LeafletDocument? capturedDoc = null;
        _repoMock
            .Setup(r => r.AddDocumentAsync(It.IsAny<LeafletDocument>(), It.IsAny<CancellationToken>()))
            .Callback<LeafletDocument, CancellationToken>((doc, _) => capturedDoc = doc);

        var handler = CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        _repoMock.Verify(
            r => r.GetByGraphItemIdAsync("drive-456", "item-xyz", It.IsAny<CancellationToken>()),
            Times.Once);
        _repoMock.Verify(
            r => r.GetBySourcePathAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);

        capturedDoc.Should().NotBeNull();
        capturedDoc!.DriveId.Should().Be("drive-456");
        capturedDoc.GraphItemId.Should().Be("item-xyz");
        response.WasDuplicate.Should().BeFalse();
        response.DocumentId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Handle_graphItemId_null_falls_back_to_getBySourcePath_upload_flow()
    {
        // Arrange
        _repoMock
            .Setup(r => r.GetByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LeafletDocument?)null);
        _repoMock
            .Setup(r => r.GetBySourcePathAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LeafletDocument?)null);

        _extractorMock.Setup(e => e.CanHandle("application/pdf")).Returns(true);
        _extractorMock
            .Setup(e => e.ExtractTextAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("text");

        var request = new IndexLeafletRequest
        {
            Content = new byte[] { 5, 6, 7 },
            Filename = "upload.pdf",
            SourcePath = "upload/some-guid/upload.pdf",
            ContentType = "application/pdf",
            // DriveId and GraphItemId intentionally omitted (upload flow)
        };

        var handler = CreateHandler();

        // Act
        await handler.Handle(request, CancellationToken.None);

        // Assert: upload flow uses GetBySourcePathAsync, never GetByGraphItemIdAsync
        _repoMock.Verify(
            r => r.GetBySourcePathAsync("upload/some-guid/upload.pdf", It.IsAny<CancellationToken>()),
            Times.Once);
        _repoMock.Verify(
            r => r.GetByGraphItemIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_graphItemId_provided_but_driveId_null_falls_back_to_getBySourcePath()
    {
        // Arrange
        _repoMock
            .Setup(r => r.GetByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LeafletDocument?)null);
        _repoMock
            .Setup(r => r.GetBySourcePathAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LeafletDocument?)null);

        _extractorMock.Setup(e => e.CanHandle("application/pdf")).Returns(true);
        _extractorMock
            .Setup(e => e.ExtractTextAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("text");

        var request = new IndexLeafletRequest
        {
            Content = new byte[] { 1, 2, 3 },
            Filename = "partial.pdf",
            SourcePath = "/inbox/partial.pdf",
            ContentType = "application/pdf",
            GraphItemId = "item-abc",
            // DriveId intentionally omitted — should not crash or call GetByGraphItemIdAsync
        };

        var handler = CreateHandler();

        // Act
        await handler.Handle(request, CancellationToken.None);

        // Assert: falls back to source path, GraphItemId lookup never called
        _repoMock.Verify(
            r => r.GetBySourcePathAsync("/inbox/partial.pdf", It.IsAny<CancellationToken>()),
            Times.Once);
        _repoMock.Verify(
            r => r.GetByGraphItemIdAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_hash_match_legacy_doc_with_graph_identity_backfills_graph_item_id()
    {
        // Arrange
        var content = new byte[] { 10, 20, 30 };
        var existingId = Guid.NewGuid();
        var legacyDoc = new LeafletDocument
        {
            Id = existingId,
            ContentHash = "anyhash",
            // DriveId/GraphItemId null — legacy row
        };

        _repoMock
            .Setup(r => r.GetByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(legacyDoc);

        var request = new IndexLeafletRequest
        {
            Content = content,
            Filename = "legacy.pdf",
            SourcePath = "/inbox/legacy.pdf",
            ContentType = "application/pdf",
            DriveId = "drive-backfill",
            GraphItemId = "item-backfill",
        };

        var handler = CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert: returned as duplicate and GraphItemId backfilled
        response.WasDuplicate.Should().BeTrue();
        response.DocumentId.Should().Be(existingId);
        _repoMock.Verify(
            r => r.UpdateGraphItemIdAsync(existingId, "drive-backfill", "item-backfill", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_happy_path_stamps_WordCount_on_document_before_AddDocumentAsync()
    {
        // Arrange
        _repoMock
            .Setup(r => r.GetByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LeafletDocument?)null);
        _repoMock
            .Setup(r => r.GetBySourcePathAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LeafletDocument?)null);

        _extractorMock.Setup(e => e.CanHandle("application/pdf")).Returns(true);
        _extractorMock
            .Setup(e => e.ExtractTextAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("alpha beta gamma delta epsilon");  // 5 words

        int? capturedWordCountAtAdd = null;
        _repoMock
            .Setup(r => r.AddDocumentAsync(It.IsAny<LeafletDocument>(), It.IsAny<CancellationToken>()))
            .Callback<LeafletDocument, CancellationToken>((doc, _) => capturedWordCountAtAdd = doc.WordCount);

        var handler = CreateHandler();
        var request = new IndexLeafletRequest
        {
            Content = new byte[] { 1, 2, 3 },
            Filename = "wc.pdf",
            SourcePath = "/inbox/wc.pdf",
            ContentType = "application/pdf",
        };

        // Act
        await handler.Handle(request, CancellationToken.None);

        // Assert
        capturedWordCountAtAdd.Should().Be(5);
    }

    [Fact]
    public async Task Handle_hash_match_doc_already_has_graph_item_id_does_not_backfill()
    {
        // Arrange
        var content = new byte[] { 10, 20, 30 };
        var existingId = Guid.NewGuid();
        var existingDoc = new LeafletDocument
        {
            Id = existingId,
            ContentHash = "anyhash",
            DriveId = "drive-existing",
            GraphItemId = "item-existing",
        };

        _repoMock
            .Setup(r => r.GetByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingDoc);

        var request = new IndexLeafletRequest
        {
            Content = content,
            Filename = "existing.pdf",
            SourcePath = "/inbox/existing.pdf",
            ContentType = "application/pdf",
            DriveId = "drive-existing",
            GraphItemId = "item-existing",
        };

        var handler = CreateHandler();

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert: no backfill call when GraphItemId already set
        response.WasDuplicate.Should().BeTrue();
        _repoMock.Verify(
            r => r.UpdateGraphItemIdAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
