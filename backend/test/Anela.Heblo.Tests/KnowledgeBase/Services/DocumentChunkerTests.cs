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
}
