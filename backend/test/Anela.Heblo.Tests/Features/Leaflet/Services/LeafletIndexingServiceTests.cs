using Anela.Heblo.Application.Features.Leaflet.Services;
using Anela.Heblo.Domain.Features.Leaflet;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;

namespace Anela.Heblo.Tests.Features.Leaflet.Services;

public class LeafletIndexingServiceTests
{
    private readonly Mock<ILeafletChunker> _chunker;
    private readonly Mock<IEmbeddingGenerator<string, Embedding<float>>> _embeddings;
    private readonly Mock<ILeafletRepository> _repo;
    private readonly Mock<ILogger<LeafletIndexingService>> _logger;
    private readonly LeafletIndexingService _service;

    public LeafletIndexingServiceTests()
    {
        _chunker = new Mock<ILeafletChunker>();
        _embeddings = new Mock<IEmbeddingGenerator<string, Embedding<float>>>();
        _repo = new Mock<ILeafletRepository>();
        _logger = new Mock<ILogger<LeafletIndexingService>>();

        _service = new LeafletIndexingService(
            _chunker.Object,
            _embeddings.Object,
            _repo.Object,
            _logger.Object);
    }

    private static LeafletDocument CreateDocument() =>
        new() { Id = Guid.NewGuid() };

    private static LeafletChunk CreateChunk(Guid documentId, int index) =>
        new()
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            ChunkIndex = index,
            Content = $"chunk content {index}",
            WordCount = 3,
        };

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
            .Setup(c => c.Chunk(It.IsAny<string>(), It.IsAny<Guid>()))
            .Returns(Array.Empty<LeafletChunk>());

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
        var chunks = new List<LeafletChunk>
        {
            CreateChunk(document.Id, 0),
            CreateChunk(document.Id, 1),
            CreateChunk(document.Id, 2),
        };

        _chunker
            .Setup(c => c.Chunk(It.IsAny<string>(), document.Id))
            .Returns(chunks);

        var generatedEmbeddings = CreateEmbeddings(3);
        _embeddings
            .Setup(e => e.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(generatedEmbeddings);

        _repo
            .Setup(r => r.AddChunksAsync(It.IsAny<IEnumerable<LeafletChunk>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

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
        var chunks = new List<LeafletChunk>
        {
            CreateChunk(document.Id, 0),
            CreateChunk(document.Id, 1),
            CreateChunk(document.Id, 2),
        };

        _chunker
            .Setup(c => c.Chunk(It.IsAny<string>(), document.Id))
            .Returns(chunks);

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
    public async Task IndexAsync_sets_WordCount_from_input_text()
    {
        // Arrange
        var document = CreateDocument();
        var text = string.Join(' ', Enumerable.Range(1, 1500).Select(i => $"word{i}"));

        var chunk = CreateChunk(document.Id, 0);
        _chunker
            .Setup(c => c.Chunk(It.IsAny<string>(), document.Id))
            .Returns(new List<LeafletChunk> { chunk });

        var singleEmbedding = CreateEmbeddings(1);
        _embeddings
            .Setup(e => e.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(singleEmbedding);

        _repo
            .Setup(r => r.AddChunksAsync(It.IsAny<IEnumerable<LeafletChunk>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _service.IndexAsync(text, document);

        // Assert
        Assert.Equal(1500, document.WordCount);
    }
}
