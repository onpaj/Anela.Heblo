using Anela.Heblo.Domain.Features.Leaflet;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Leaflet.Services;

public class LeafletIndexingService : ILeafletIndexingService
{
    private readonly ILeafletChunker _chunker;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddings;
    private readonly ILeafletRepository _repo;
    private readonly ILogger<LeafletIndexingService> _logger;

    public LeafletIndexingService(
        ILeafletChunker chunker,
        IEmbeddingGenerator<string, Embedding<float>> embeddings,
        ILeafletRepository repo,
        ILogger<LeafletIndexingService> logger)
    {
        _chunker = chunker;
        _embeddings = embeddings;
        _repo = repo;
        _logger = logger;
    }

    public async Task<int> IndexAsync(string text, LeafletDocument document, CancellationToken ct = default)
    {
        var chunks = _chunker.Chunk(text, document.Id);
        if (chunks.Count == 0)
        {
            _logger.LogWarning("Leaflet {DocumentId} produced zero chunks; skipping indexing", document.Id);
            return 0;
        }

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
