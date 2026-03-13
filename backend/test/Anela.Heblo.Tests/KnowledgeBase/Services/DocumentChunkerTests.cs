using Anela.Heblo.Application.Features.KnowledgeBase;
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.Services;

public class DocumentChunkerTests
{
    private static DocumentChunker CreateChunker(int chunkSize = 20, int overlapWords = 2)
    {
        var options = Options.Create(new KnowledgeBaseOptions
        {
            ChunkSize = chunkSize,
            ChunkOverlapTokens = overlapWords
        });
        return new DocumentChunker(options);
    }

    [Fact]
    public void Chunk_ShortText_ReturnsSingleChunk()
    {
        var chunker = CreateChunker(chunkSize: 200);
        var text = "This is a short text.";
        var chunks = chunker.Chunk(text);
        Assert.Single(chunks);
        Assert.Equal(text, chunks[0]);
    }

    [Fact]
    public void Chunk_EmptyText_ReturnsEmpty()
    {
        var chunker = CreateChunker();
        var chunks = chunker.Chunk(string.Empty);
        Assert.Empty(chunks);
    }

    [Fact]
    public void Chunk_WhitespaceOnly_ReturnsEmpty()
    {
        var chunker = CreateChunker();
        var chunks = chunker.Chunk("   \n\t  ");
        Assert.Empty(chunks);
    }

    [Fact]
    public void Chunk_LongText_ReturnsMultipleChunksWithOverlap()
    {
        var chunker = CreateChunker(chunkSize: 5, overlapWords: 1);
        // 15 words → chunkSize=5, overlap=1, step=4
        // chunk 0: words 0-4 ("one two three four five")
        // chunk 1: words 4-8 ("five six seven eight nine") ← "five" is the overlap
        var text = "one two three four five six seven eight nine ten eleven twelve thirteen fourteen fifteen";
        var chunks = chunker.Chunk(text);
        Assert.True(chunks.Count > 1);
        Assert.Contains("five", chunks[1]);
    }

    [Fact]
    public void Chunk_CzechDiacritics_PreservesCharacters()
    {
        var chunker = CreateChunker(chunkSize: 5, overlapWords: 1);
        var text = "Přírodní kosmetika obsahuje šípkový olej česnek švestky třezalku meduňku heřmánek levanduli";
        var chunks = chunker.Chunk(text);

        Assert.NotEmpty(chunks);
        // All Czech characters should be preserved intact
        Assert.Contains("šípkový", string.Join(" ", chunks));
        Assert.Contains("heřmánek", string.Join(" ", chunks));
    }

    [Fact]
    public void Chunk_VeryLongDocument_10000Words_ReturnsExpectedChunkCount()
    {
        const int chunkSize = 512;
        const int overlapWords = 50;
        const int wordCount = 10000;
        var chunker = CreateChunker(chunkSize: chunkSize, overlapWords: overlapWords);

        var words = Enumerable.Range(1, wordCount).Select(i => $"word{i}");
        var text = string.Join(" ", words);

        var chunks = chunker.Chunk(text);

        // Each chunk advances by (chunkSize - overlapWords) = 462 words
        // Expected: ceil(10000 / 462) ≈ 22 chunks (may vary by ±1 due to boundary)
        Assert.True(chunks.Count >= 20 && chunks.Count <= 25,
            $"Expected ~22 chunks for 10000 words but got {chunks.Count}");

        // Every chunk should have at most chunkSize words
        foreach (var chunk in chunks)
        {
            var wc = chunk.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            Assert.True(wc <= chunkSize, $"Chunk has {wc} words, expected <= {chunkSize}");
        }
    }

    [Fact]
    public void Chunk_ExactlyChunkSizeWords_ReturnsSingleChunk()
    {
        const int chunkSize = 10;
        var chunker = CreateChunker(chunkSize: chunkSize, overlapWords: 2);

        var words = Enumerable.Range(1, chunkSize).Select(i => $"w{i}");
        var text = string.Join(" ", words);

        var chunks = chunker.Chunk(text);

        Assert.Single(chunks);
        Assert.Equal(chunkSize, chunks[0].Split(' ').Length);
    }

    [Fact]
    public void Chunk_ChunkSizePlusOneWord_ReturnsTwoChunks()
    {
        const int chunkSize = 10;
        const int overlap = 2;
        var chunker = CreateChunker(chunkSize: chunkSize, overlapWords: overlap);

        var words = Enumerable.Range(1, chunkSize + 1).Select(i => $"w{i}");
        var text = string.Join(" ", words);

        var chunks = chunker.Chunk(text);

        Assert.Equal(2, chunks.Count);
        // Second chunk should start with the overlapping words
        var secondChunkWords = chunks[1].Split(' ');
        Assert.Equal($"w{chunkSize - overlap + 1}", secondChunkWords[0]);
    }
}
