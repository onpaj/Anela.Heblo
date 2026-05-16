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
    private readonly ILeafletChunkSummarizer _summarizer;
    private readonly ILeafletDocumentRepository _repo;
    private readonly ILogger<LeafletIndexingService> _logger;
    private readonly LeafletOptions _options;

    public LeafletIndexingService(
        IWordWindowChunker chunker,
        IEmbeddingGenerator<string, Embedding<float>> embeddings,
        ILeafletChunkSummarizer summarizer,
        ILeafletDocumentRepository repo,
        ILogger<LeafletIndexingService> logger,
        IOptions<LeafletOptions> options)
    {
        _chunker = chunker;
        _embeddings = embeddings;
        _summarizer = summarizer;
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

        var chunks = new List<LeafletChunk>();
        for (var i = 0; i < chunkTexts.Count; i++)
        {
            var content = chunkTexts[i];
            var summary = await _summarizer.SummarizeAsync(content, ct);
            chunks.Add(new LeafletChunk
            {
                Id = Guid.NewGuid(),
                DocumentId = document.Id,
                ChunkIndex = i,
                Content = content,
                Summary = summary,
                WordCount = content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length,
                Embedding = Array.Empty<float>(),
            });
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

        await _repo.AddChunksAsync(chunks, ct);
        return chunks.Count;
    }
}
