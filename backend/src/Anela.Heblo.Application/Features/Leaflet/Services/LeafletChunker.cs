using Anela.Heblo.Domain.Features.Leaflet;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Leaflet.Services;

public class LeafletChunker : ILeafletChunker
{
    private readonly LeafletOptions _options;

    public LeafletChunker(IOptions<LeafletOptions> options)
    {
        _options = options.Value;
    }

    public IReadOnlyList<LeafletChunk> Chunk(string text, Guid documentId)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<LeafletChunk>();

        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var size = _options.ChunkSizeWords;
        var overlap = _options.ChunkOverlapWords;
        var step = Math.Max(1, size - overlap);
        var chunks = new List<LeafletChunk>();
        var idx = 0;

        for (var start = 0; start < words.Length; start += step)
        {
            var take = Math.Min(size, words.Length - start);
            var slice = string.Join(' ', words, start, take);
            chunks.Add(new LeafletChunk
            {
                Id = Guid.NewGuid(),
                DocumentId = documentId,
                ChunkIndex = idx++,
                Content = slice,
                WordCount = take,
                Embedding = Array.Empty<float>(),
            });
            if (start + size >= words.Length)
                break;
        }

        return chunks;
    }
}
