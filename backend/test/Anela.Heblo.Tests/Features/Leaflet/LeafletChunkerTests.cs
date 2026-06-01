using Anela.Heblo.Application.Shared.Rag;
using Xunit;

namespace Anela.Heblo.Tests.Features.Leaflet;

public class LeafletChunkerTests
{
    private static WordWindowChunker CreateChunker() => new();

    private static string BuildWords(int count) =>
        string.Join(' ', Enumerable.Range(1, count).Select(i => $"word{i}"));

    [Fact]
    public void Chunk_empty_input_returns_empty()
    {
        // Arrange
        var chunker = CreateChunker();

        // Act
        var result = chunker.Chunk(string.Empty, size: 800, overlap: 80);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void Chunk_short_input_below_size_returns_one_chunk()
    {
        // Arrange
        var chunker = CreateChunker();
        var text = BuildWords(100);

        // Act
        var result = chunker.Chunk(text, size: 800, overlap: 80);

        // Assert
        Assert.Single(result);
        var wordCount = result[0].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        Assert.Equal(100, wordCount);
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
        var chunker = CreateChunker();
        var text = BuildWords(900);

        // Act
        var result = chunker.Chunk(text, size: 800, overlap: 80);

        // Assert
        Assert.Equal(2, result.Count);
        var wc0 = result[0].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        var wc1 = result[1].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        Assert.Equal(800, wc0);
        Assert.Equal(180, wc1);
    }

    [Fact]
    public void Chunk_overlap_appears_in_consecutive_chunks()
    {
        // Arrange
        var chunker = CreateChunker();
        var text = BuildWords(900);
        var documentId = Guid.NewGuid();

        // Act
        var result = chunker.Chunk(text, size: 800, overlap: 80);

        // Assert — the last 80 words of chunk 0 should appear at the start of chunk 1
        var chunk0Words = result[0].Split(' ');
        var chunk1Words = result[1].Split(' ');
        Assert.Equal(chunk0Words[720], chunk1Words[0]);
    }
}
