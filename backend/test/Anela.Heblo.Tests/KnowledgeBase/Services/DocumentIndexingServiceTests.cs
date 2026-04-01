using Anela.Heblo.Application.Features.KnowledgeBase;
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.Services;

public class DocumentIndexingServiceTests
{
    private readonly Mock<IDocumentTextExtractor> _pdfExtractor;
    private readonly Mock<IKnowledgeBaseRepository> _repository;
    private readonly Mock<IIndexingStrategy> _kbStrategy;
    private readonly Mock<IIndexingStrategy> _conversationStrategy;
    private readonly DocumentIndexingService _service;
    private readonly KnowledgeBaseOptions _options;

    public DocumentIndexingServiceTests()
    {
        _pdfExtractor = new Mock<IDocumentTextExtractor>();
        _pdfExtractor.Setup(e => e.CanHandle("application/pdf")).Returns(true);
        _pdfExtractor
            .Setup(e => e.ExtractTextAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("word1 word2 word3");

        _repository = new Mock<IKnowledgeBaseRepository>();

        _kbStrategy = new Mock<IIndexingStrategy>();
        _kbStrategy.Setup(s => s.Supports(DocumentType.KnowledgeBase)).Returns(true);
        _kbStrategy.Setup(s => s.Supports(DocumentType.Conversation)).Returns(false);
        _kbStrategy
            .Setup(s => s.CreateChunksAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KnowledgeBaseChunk>
            {
                new() { Id = Guid.NewGuid(), DocumentId = Guid.NewGuid(), ChunkIndex = 0, Content = "chunk", Embedding = [0.1f] }
            });

        _conversationStrategy = new Mock<IIndexingStrategy>();
        _conversationStrategy.Setup(s => s.Supports(DocumentType.Conversation)).Returns(true);
        _conversationStrategy.Setup(s => s.Supports(DocumentType.KnowledgeBase)).Returns(false);
        _conversationStrategy
            .Setup(s => s.CreateChunksAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<KnowledgeBaseChunk>
            {
                new() { Id = Guid.NewGuid(), DocumentId = Guid.NewGuid(), ChunkIndex = 0, Content = "chunk", Embedding = [0.1f] }
            });

        _options = new KnowledgeBaseOptions();
        var preprocessor = new ChatTranscriptPreprocessor(Options.Create(_options));

        _service = new DocumentIndexingService(
            new[] { _pdfExtractor.Object },
            _repository.Object,
            preprocessor,
            new[] { _kbStrategy.Object, _conversationStrategy.Object });
    }

    [Fact]
    public async Task IndexChunksAsync_KnowledgeBaseDocument_UsesKbStrategy()
    {
        var doc = new KnowledgeBaseDocument { Id = Guid.NewGuid(), DocumentType = DocumentType.KnowledgeBase };

        await _service.IndexChunksAsync([], "application/pdf", doc, CancellationToken.None);

        _kbStrategy.Verify(
            s => s.CreateChunksAsync(It.IsAny<string>(), doc.Id, It.IsAny<CancellationToken>()),
            Times.Once);
        _conversationStrategy.Verify(
            s => s.CreateChunksAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task IndexChunksAsync_ConversationDocument_UsesConversationStrategy()
    {
        _pdfExtractor
            .Setup(e => e.ExtractTextAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("conversation text");

        var doc = new KnowledgeBaseDocument { Id = Guid.NewGuid(), DocumentType = DocumentType.Conversation };

        await _service.IndexChunksAsync([], "application/pdf", doc, CancellationToken.None);

        _conversationStrategy.Verify(
            s => s.CreateChunksAsync(It.IsAny<string>(), doc.Id, It.IsAny<CancellationToken>()),
            Times.Once);
        _kbStrategy.Verify(
            s => s.CreateChunksAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task IndexChunksAsync_ChunksPersistedAndStatusSetToIndexed()
    {
        var doc = new KnowledgeBaseDocument { Id = Guid.NewGuid(), DocumentType = DocumentType.KnowledgeBase };

        await _service.IndexChunksAsync([], "application/pdf", doc, CancellationToken.None);

        _repository.Verify(
            r => r.AddChunksAsync(It.IsAny<IEnumerable<KnowledgeBaseChunk>>(), It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.Equal(DocumentStatus.Indexed, doc.Status);
        Assert.NotNull(doc.IndexedAt);
    }

    [Fact]
    public async Task IndexChunksAsync_UnsupportedContentType_ThrowsNotSupportedException()
    {
        var doc = new KnowledgeBaseDocument { Id = Guid.NewGuid() };

        await Assert.ThrowsAsync<NotSupportedException>(
            () => _service.IndexChunksAsync([], "image/png", doc, CancellationToken.None));
    }

    [Fact]
    public async Task IndexChunksAsync_PreprocessorStripsBoilerplateBeforeStrategy()
    {
        const string boilerplateText =
            "datum: 04.11.2025 02:31\nzákazník: Zákazník-0001\nAnela: bisabolol je vhodný pro citlivou pleť";

        _pdfExtractor
            .Setup(e => e.ExtractTextAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(boilerplateText);

        string? capturedCleanText = null;
        _kbStrategy
            .Setup(s => s.CreateChunksAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, CancellationToken>((text, _, _) => capturedCleanText = text)
            .ReturnsAsync(new List<KnowledgeBaseChunk>());

        var doc = new KnowledgeBaseDocument { Id = Guid.NewGuid(), DocumentType = DocumentType.KnowledgeBase };
        await _service.IndexChunksAsync([], "application/pdf", doc, CancellationToken.None);

        Assert.NotNull(capturedCleanText);
        Assert.DoesNotContain("datum:", capturedCleanText);
    }
}
