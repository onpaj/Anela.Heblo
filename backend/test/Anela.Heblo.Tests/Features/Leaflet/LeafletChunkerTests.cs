using Anela.Heblo.Application.Features.Leaflet;
using Anela.Heblo.Application.Features.Leaflet.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace Anela.Heblo.Tests.Features.Leaflet;

public class LeafletChunkerTests
{
    private static LeafletChunker CreateChunker(int chunkSizeWords = 800, int chunkOverlapWords = 80)
    {
        var options = Options.Create(new LeafletOptions
        {
            ChunkSizeWords = chunkSizeWords,
            ChunkOverlapWords = chunkOverlapWords,
        });
        return new LeafletChunker(options);
    }

    private static string BuildWords(int count) =>
        string.Join(' ', Enumerable.Range(1, count).Select(i => $"word{i}"));

    [Fact]
    public void Chunk_empty_input_returns_empty()
    {
        // Arrange
        var chunker = CreateChunker();

        // Act
        var result = chunker.Chunk(string.Empty, Guid.NewGuid());

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void Chunk_short_input_below_size_returns_one_chunk()
    {
        // Arrange
        var chunker = CreateChunker(chunkSizeWords: 800, chunkOverlapWords: 80);
        var text = BuildWords(100);
        var documentId = Guid.NewGuid();

        // Act
        var result = chunker.Chunk(text, documentId);

        // Assert
        Assert.Single(result);
        Assert.Equal(100, result[0].WordCount);
        Assert.Equal(0, result[0].ChunkIndex);
    }

    [Fact]
    public void Chunk_long_input_creates_multiple_overlapping_chunks()
    {
        // Arrange
        // size=800, overlap=80, step=720
        // start=0: take=min(800,900)=800 → chunk0 (words 0-799)
        // 0+800=800 < 900 → continue
        // start=720: take=min(800,900-720)=min(800,180)=180 → chunk1 (words 720-899)
        // 720+800=1520 >= 900 → break
        var chunker = CreateChunker(chunkSizeWords: 800, chunkOverlapWords: 80);
        var text = BuildWords(900);
        var documentId = Guid.NewGuid();

        // Act
        var result = chunker.Chunk(text, documentId);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal(0, result[0].ChunkIndex);
        Assert.Equal(800, result[0].WordCount);
        Assert.Equal(1, result[1].ChunkIndex);
        Assert.Equal(180, result[1].WordCount);
    }

    [Fact]
    public void Chunk_assigns_DocumentId_to_each_chunk()
    {
        // Arrange
        var chunker = CreateChunker(chunkSizeWords: 800, chunkOverlapWords: 80);
        var documentId = Guid.NewGuid();
        var text = BuildWords(900);

        // Act
        var result = chunker.Chunk(text, documentId);

        // Assert
        Assert.All(result, chunk => Assert.Equal(documentId, chunk.DocumentId));
    }
}
