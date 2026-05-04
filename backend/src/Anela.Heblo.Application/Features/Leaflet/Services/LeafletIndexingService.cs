using Anela.Heblo.Application.Shared.Rag;
using Anela.Heblo.Domain.Features.Leaflet;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Leaflet.Services;

public class LeafletIndexingService : ILeafletIndexingService
{
    private readonly IWordWindowChunker _chunker;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddings;
    private readonly ILeafletRepository _repo;
    private readonly ILogger<LeafletIndexingService> _logger;
    private readonly LeafletOptions _options;

    public LeafletIndexingService(
        IWordWindowChunker chunker,
        IEmbeddingGenerator<string, Embedding<float>> embeddings,
        ILeafletRepository repo,
        ILogger<LeafletIndexingService> logger,
        IOptions<LeafletOptions> options)
    {
        _chunker = chunker;
        _embeddings = embeddings;
        _repo = repo;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<int> IndexAsync(string text, LeafletDocument document, CancellationToken ct = default)
    {
        var chunkTexts = _chunker.Chunk(text, _options.ChunkSize, _options.ChunkOverlap);
        if (chunkTexts.Count == 0)
        {
            _logger.LogWarning("Leaflet {DocumentId} produced zero chunks; skipping indexing", document.Id);
            return 0;
        }

        var chunks = chunkTexts.Select((content, idx) => new LeafletChunk
        {
            Id = Guid.NewGuid(),
            DocumentId = document.Id,
            ChunkIndex = idx,
            Content = content,
            WordCount = content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length,
            Embedding = Array.Empty<float>(),
        }).ToList();

        var inputs = chunks.Select(c => c.Content).ToList();
        var generated = await _embeddings.GenerateAsync(inputs, cancellationToken: ct);
        var vectors = generated.ToList();

        if (vectors.Count != chunks.Count)
        {
            throw new InvalidOperationException(
                $"Embedding count {vectors.Count} does not match chunk count {chunks.Count}");
        }

        for (var i = 0; i < chunks.Count; i++)
        {
            chunks[i].Embedding = vectors[i].Vector.ToArray();
        }

        document.WordCount = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        await _repo.AddChunksAsync(chunks, ct);
        return chunks.Count;
    }
}
