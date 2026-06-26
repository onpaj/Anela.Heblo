using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Application.Features.Leaflet.Services;
using Anela.Heblo.Application.Features.Leaflet.UseCases.IndexLeaflet;
using Anela.Heblo.Domain.Features.Leaflet;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Leaflet.UseCases;

/// <summary>
/// Tests for IndexLeafletHandler document status transitions:
/// Processing → Indexed on success, Processing → Failed on extractor error.
/// </summary>
public class IndexLeafletStatusTransitionTests
{
    private readonly Mock<ILeafletDocumentRepository> _repoMock = new();
    private readonly Mock<ILeafletIndexingService> _indexingMock = new();
    private readonly Mock<IDocumentTextExtractor> _extractorMock = new();

    public IndexLeafletStatusTransitionTests()
    {
        _repoMock
            .Setup(r => r.GetByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LeafletDocument?)null);
        _repoMock
            .Setup(r => r.GetBySourcePathAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LeafletDocument?)null);

        _extractorMock.Setup(e => e.CanHandle("application/pdf")).Returns(true);
        _extractorMock
            .Setup(e => e.ExtractTextAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("extracted text content");

        _indexingMock
            .Setup(s => s.IndexAsync(It.IsAny<string>(), It.IsAny<LeafletDocument>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);
    }

    private IndexLeafletHandler CreateHandler() =>
        new(
            _repoMock.Object,
            new[] { _extractorMock.Object },
            _indexingMock.Object,
            NullLogger<IndexLeafletHandler>.Instance);

    private IndexLeafletRequest CreateRequest() =>
        new()
        {
            Content = new byte[] { 1, 2, 3 },
            Filename = "test.pdf",
            SourcePath = "/inbox/test.pdf",
            ContentType = "application/pdf",
        };

    [Fact]
    public async Task Handle_new_document_sets_Processing_status_before_indexing_then_Indexed_on_success()
    {
        // Arrange
        LeafletDocument? capturedDoc = null;
        _repoMock
            .Setup(r => r.AddDocumentAsync(It.IsAny<LeafletDocument>(), It.IsAny<CancellationToken>()))
            .Callback<LeafletDocument, CancellationToken>((doc, _) => capturedDoc = doc);

        LeafletDocumentStatus? capturedFinalStatus = null;
        _repoMock
            .Setup(r => r.UpdateStatusAsync(It.IsAny<Guid>(), It.IsAny<LeafletDocumentStatus>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, LeafletDocumentStatus, DateTime?, CancellationToken>((_, status, _, _) => capturedFinalStatus = status)
            .Returns(Task.CompletedTask);

        var handler = CreateHandler();

        // Act
        var response = await handler.Handle(CreateRequest(), CancellationToken.None);

        // Assert: document was created with Processing status
        capturedDoc.Should().NotBeNull();
        capturedDoc!.Status.Should().Be(LeafletDocumentStatus.Processing);

        // Assert: final status updated to Indexed
        capturedFinalStatus.Should().Be(LeafletDocumentStatus.Indexed);
        response.Status.Should().Be(LeafletDocumentStatus.Indexed);
    }

    [Fact]
    public async Task Handle_indexing_failure_updates_status_to_Failed_and_rethrows()
    {
        // Arrange
        var indexingException = new InvalidOperationException("Embedding service unavailable");
        _indexingMock
            .Setup(s => s.IndexAsync(It.IsAny<string>(), It.IsAny<LeafletDocument>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(indexingException);

        LeafletDocumentStatus? capturedFailedStatus = null;
        _repoMock
            .Setup(r => r.UpdateStatusAsync(It.IsAny<Guid>(), It.IsAny<LeafletDocumentStatus>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, LeafletDocumentStatus, DateTime?, CancellationToken>((_, status, _, _) => capturedFailedStatus = status)
            .Returns(Task.CompletedTask);

        var handler = CreateHandler();

        // Act
        var act = () => handler.Handle(CreateRequest(), CancellationToken.None);

        // Assert: original exception is rethrown
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Embedding service unavailable");

        // Assert: status was set to Failed
        capturedFailedStatus.Should().Be(LeafletDocumentStatus.Failed);
    }

    [Fact]
    public async Task Handle_indexing_failure_rethrows_even_if_status_update_also_fails()
    {
        // Arrange
        _indexingMock
            .Setup(s => s.IndexAsync(It.IsAny<string>(), It.IsAny<LeafletDocument>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Indexing failed"));

        _repoMock
            .Setup(r => r.UpdateStatusAsync(It.IsAny<Guid>(), LeafletDocumentStatus.Failed, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Database save failed"));

        var handler = CreateHandler();

        // Act
        var act = () => handler.Handle(CreateRequest(), CancellationToken.None);

        // Assert: original indexing exception is rethrown, not the status-save exception
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Indexing failed");
    }
}
