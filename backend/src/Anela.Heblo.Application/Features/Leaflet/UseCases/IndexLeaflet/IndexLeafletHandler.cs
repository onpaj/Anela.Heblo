using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Application.Features.Leaflet.Services;
using Anela.Heblo.Domain.Features.Leaflet;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Leaflet.UseCases.IndexLeaflet;

public class IndexLeafletHandler : IRequestHandler<IndexLeafletRequest, IndexLeafletResponse>
{
    private readonly ILeafletRepository _repo;
    private readonly IEnumerable<IDocumentTextExtractor> _extractors;
    private readonly ILeafletIndexingService _indexing;
    private readonly ILogger<IndexLeafletHandler> _logger;

    public IndexLeafletHandler(
        ILeafletRepository repo,
        IEnumerable<IDocumentTextExtractor> extractors,
        ILeafletIndexingService indexing,
        ILogger<IndexLeafletHandler> logger)
    {
        _repo = repo;
        _extractors = extractors;
        _indexing = indexing;
        _logger = logger;
    }

    public async Task<IndexLeafletResponse> Handle(IndexLeafletRequest request, CancellationToken ct)
    {
        var hash = ComputeHash(request.Content);

        var existing = await _repo.GetByHashAsync(hash, ct);
        if (existing is not null)
        {
            _logger.LogInformation(
                "Duplicate leaflet content detected, hash={Hash}, document={Id}",
                hash, existing.Id);
            return new IndexLeafletResponse { DocumentId = existing.Id, WasDuplicate = true };
        }

        var byPath = await _repo.GetBySourcePathAsync(request.SourcePath, ct);
        if (byPath is not null)
        {
            _logger.LogInformation("Source path collision, replacing old document {Id}", byPath.Id);
            await _repo.DeleteDocumentAsync(byPath.Id, ct);
        }

        var extractor = _extractors.FirstOrDefault(e => e.CanHandle(request.ContentType))
            ?? throw new NotSupportedException($"No extractor for content type '{request.ContentType}'");

        var text = await extractor.ExtractTextAsync(request.Content, ct);

        var doc = new LeafletDocument
        {
            Id = Guid.NewGuid(),
            Filename = request.Filename,
            SourcePath = request.SourcePath,
            ContentType = request.ContentType,
            ContentHash = hash,
            IngestedAt = DateTime.UtcNow,
            WordCount = 0,
        };

        await _repo.AddDocumentAsync(doc, ct);
        await _repo.SaveChangesAsync(ct);

        var chunkCount = await _indexing.IndexAsync(text, doc, ct);
        await _repo.SaveChangesAsync(ct);

        return new IndexLeafletResponse { DocumentId = doc.Id, WasDuplicate = false, ChunkCount = chunkCount };
    }

    private static string ComputeHash(byte[] content)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = sha.ComputeHash(content);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
