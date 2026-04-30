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
    private readonly Mock<ILeafletRepository> _repoMock;
    private readonly Mock<ILeafletIndexingService> _indexingMock;
    private readonly Mock<IDocumentTextExtractor> _extractorMock;

    public IndexLeafletHandlerTests()
    {
        _repoMock = new Mock<ILeafletRepository>();
        _indexingMock = new Mock<ILeafletIndexingService>();
        _extractorMock = new Mock<IDocumentTextExtractor>();
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
}
