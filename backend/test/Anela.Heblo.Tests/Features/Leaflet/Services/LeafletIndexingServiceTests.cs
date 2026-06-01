using Anela.Heblo.Application.Features.Leaflet;
using Anela.Heblo.Application.Features.Leaflet.Services;
using Anela.Heblo.Application.Shared.Rag;
using Anela.Heblo.Domain.Features.Leaflet;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Anela.Heblo.Tests.Features.Leaflet.Services;

public class LeafletIndexingServiceTests
{
    private readonly Mock<IWordWindowChunker> _chunker;
    private readonly Mock<IEmbeddingGenerator<string, Embedding<float>>> _embeddings;
    private readonly Mock<ILeafletChunkSummarizer> _summarizer;
    private readonly Mock<ILeafletDocumentRepository> _repo;
    private readonly Mock<ILogger<LeafletIndexingService>> _logger;
    private readonly LeafletOptions _options;
    private readonly LeafletIndexingService _service;

    public LeafletIndexingServiceTests()
    {
        _chunker = new Mock<IWordWindowChunker>();
        _embeddings = new Mock<IEmbeddingGenerator<string, Embedding<float>>>();
        _summarizer = new Mock<ILeafletChunkSummarizer>();
        _repo = new Mock<ILeafletDocumentRepository>();
        _logger = new Mock<ILogger<LeafletIndexingService>>();
        _options = new LeafletOptions { ChunkSize = 800, ChunkOverlap = 80 };

        _summarizer
            .Setup(s => s.SummarizeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string text, CancellationToken _) => text);

        _service = new LeafletIndexingService(
            _chunker.Object,
            _embeddings.Object,
            _summarizer.Object,
            _repo.Object,
            _logger.Object,
            Options.Create(_options));
    }

    private static LeafletDocument CreateDocument() =>
        new() { Id = Guid.NewGuid() };

    private static GeneratedEmbeddings<Embedding<float>> CreateEmbeddings(int count, float startValue = 0.1f) =>
        new(Enumerable.Range(0, count)
            .Select(i => new Embedding<float>(new ReadOnlyMemory<float>(
                [startValue + i * 0.1f, startValue + i * 0.1f + 0.01f, startValue + i * 0.1f + 0.02f])))
            .ToList());

    [Fact]
    public async Task IndexAsync_no_chunks_skips_persistence()
    {
        // Arrange
        var document = CreateDocument();
        _chunker
            .Setup(c => c.Chunk(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(Array.Empty<string>());

        // Act
        await _service.IndexAsync(string.Empty, document);

        // Assert
        _repo.Verify(
            r => r.AddChunksAsync(It.IsAny<IEnumerable<LeafletChunk>>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _logger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("zero chunks")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task IndexAsync_assigns_embeddings_to_each_chunk()
    {
        // Arrange
        var document = CreateDocument();
        var chunkTexts = new[] { "chunk content 0", "chunk content 1", "chunk content 2" };

        _chunker
            .Setup(c => c.Chunk(It.IsAny<string>(), _options.ChunkSize, _options.ChunkOverlap))
            .Returns(chunkTexts);

        var generatedEmbeddings = CreateEmbeddings(3);
        _embeddings
            .Setup(e => e.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(generatedEmbeddings);

        IEnumerable<LeafletChunk>? capturedChunks = null;
        _repo
            .Setup(r => r.AddChunksAsync(It.IsAny<IEnumerable<LeafletChunk>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<LeafletChunk>, CancellationToken>((c, _) => capturedChunks = c.ToList())
            .Returns(Task.CompletedTask);

        // Act
        await _service.IndexAsync("some text content", document);

        // Assert
        Assert.NotNull(capturedChunks);
        var persistedChunks = capturedChunks.ToList();
        Assert.Equal(3, persistedChunks.Count);

        var expectedVectors = generatedEmbeddings.ToList();
        for (var i = 0; i < persistedChunks.Count; i++)
        {
            Assert.Equal(expectedVectors[i].Vector.ToArray(), persistedChunks[i].Embedding);
        }
    }

    [Fact]
    public async Task IndexAsync_throws_on_count_mismatch()
    {
        // Arrange
        var document = CreateDocument();
        var chunkTexts = new[] { "chunk 0", "chunk 1", "chunk 2" };

        _chunker
            .Setup(c => c.Chunk(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(chunkTexts);

        var twoEmbeddings = CreateEmbeddings(2);
        _embeddings
            .Setup(e => e.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(twoEmbeddings);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.IndexAsync("some text content", document));
    }

    [Fact]
    public async Task IndexAsync_builds_chunks_with_correct_metadata()
    {
        // Arrange
        var document = CreateDocument();
        var chunkTexts = new[] { "hello world", "world foo bar" };

        _chunker
            .Setup(c => c.Chunk(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(chunkTexts);

        var generatedEmbeddings = CreateEmbeddings(2);
        _embeddings
            .Setup(e => e.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(generatedEmbeddings);

        IEnumerable<LeafletChunk>? capturedChunks = null;
        _repo
            .Setup(r => r.AddChunksAsync(It.IsAny<IEnumerable<LeafletChunk>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<LeafletChunk>, CancellationToken>((c, _) => capturedChunks = c.ToList())
            .Returns(Task.CompletedTask);

        // Act
        await _service.IndexAsync("hello world world foo bar", document);

        // Assert
        Assert.NotNull(capturedChunks);
        var chunks = capturedChunks.ToList();
        Assert.Equal(2, chunks.Count);

        Assert.Equal(document.Id, chunks[0].DocumentId);
        Assert.Equal(0, chunks[0].ChunkIndex);
        Assert.Equal("hello world", chunks[0].Content);
        Assert.Equal(2, chunks[0].WordCount);

        Assert.Equal(document.Id, chunks[1].DocumentId);
        Assert.Equal(1, chunks[1].ChunkIndex);
        Assert.Equal("world foo bar", chunks[1].Content);
        Assert.Equal(3, chunks[1].WordCount);
    }

    [Fact]
    public async Task IndexAsync_sets_Summary_from_summarizer()
    {
        // Arrange
        var document = CreateDocument();
        var chunkTexts = new[] { "chunk one", "chunk two" };

        _chunker
            .Setup(c => c.Chunk(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(chunkTexts);

        _summarizer
            .Setup(s => s.SummarizeAsync("chunk one", It.IsAny<CancellationToken>()))
            .ReturnsAsync("summary one");
        _summarizer
            .Setup(s => s.SummarizeAsync("chunk two", It.IsAny<CancellationToken>()))
            .ReturnsAsync("summary two");

        var generatedEmbeddings = CreateEmbeddings(2);
        _embeddings
            .Setup(e => e.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(generatedEmbeddings);

        IEnumerable<LeafletChunk>? capturedChunks = null;
        _repo
            .Setup(r => r.AddChunksAsync(It.IsAny<IEnumerable<LeafletChunk>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<LeafletChunk>, CancellationToken>((c, _) => capturedChunks = c.ToList())
            .Returns(Task.CompletedTask);

        // Act
        await _service.IndexAsync("chunk one chunk two", document);

        // Assert
        capturedChunks.Should().NotBeNull();
        var chunks = capturedChunks.ToList();
        chunks[0].Summary.Should().Be("summary one");
        chunks[1].Summary.Should().Be("summary two");
    }
}
