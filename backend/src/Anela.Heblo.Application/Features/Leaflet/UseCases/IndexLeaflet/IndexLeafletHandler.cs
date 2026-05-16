using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Application.Features.Leaflet.Services;
using Anela.Heblo.Domain.Features.Leaflet;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Leaflet.UseCases.IndexLeaflet;

public class IndexLeafletHandler : IRequestHandler<IndexLeafletRequest, IndexLeafletResponse>
{
    private readonly ILeafletDocumentRepository _repo;
    private readonly IEnumerable<IDocumentTextExtractor> _extractors;
    private readonly ILeafletIndexingService _indexing;
    private readonly ILogger<IndexLeafletHandler> _logger;

    public IndexLeafletHandler(
        ILeafletDocumentRepository repo,
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
        var useGraphIdentity = !string.IsNullOrEmpty(request.GraphItemId) && !string.IsNullOrEmpty(request.DriveId);

        var existing = await _repo.GetByHashAsync(hash, ct);
        if (existing is not null)
        {
            _logger.LogInformation(
                "Duplicate leaflet content detected, hash={Hash}, document={Id}",
                hash, existing.Id);

            if (useGraphIdentity && existing.GraphItemId is null)
            {
                _logger.LogInformation(
                    "Backfilling DriveId/GraphItemId for legacy leaflet document {Id}",
                    existing.Id);
                await _repo.UpdateGraphItemIdAsync(existing.Id, request.DriveId!, request.GraphItemId!, ct);
            }

            return new IndexLeafletResponse
            {
                DocumentId = existing.Id,
                WasDuplicate = true,
                Status = existing.Status,
                Filename = existing.Filename,
                ContentType = existing.ContentType,
                IngestedAt = existing.IngestedAt,
                IndexedAt = existing.IndexedAt,
            };
        }

        var existingByIdentity = useGraphIdentity
            ? await _repo.GetByGraphItemIdAsync(request.DriveId!, request.GraphItemId!, ct)
            : await _repo.GetBySourcePathAsync(request.SourcePath, ct);

        if (existingByIdentity is not null)
        {
            _logger.LogInformation("Replacing old document {Id} (identity match)", existingByIdentity.Id);
            await _repo.DeleteDocumentAsync(existingByIdentity.Id, ct);
        }

        var extractor = _extractors.FirstOrDefault(e => e.CanHandle(request.ContentType))
            ?? throw new NotSupportedException($"No extractor for content type '{request.ContentType}'");

        var text = await extractor.ExtractTextAsync(request.Content, ct);
        var wordCount = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

        var doc = new LeafletDocument
        {
            Id = Guid.NewGuid(),
            Filename = request.Filename,
            SourcePath = request.SourcePath,
            ContentType = request.ContentType,
            ContentHash = hash,
            IngestedAt = DateTime.UtcNow,
            WordCount = wordCount,
            DriveId = useGraphIdentity ? request.DriveId : null,
            GraphItemId = useGraphIdentity ? request.GraphItemId : null,
            Status = LeafletDocumentStatus.Processing,
        };

        await _repo.AddDocumentAsync(doc, ct);

        try
        {
            var chunkCount = await _indexing.IndexAsync(text, doc, ct);

            var indexedAt = DateTime.UtcNow;
            await _repo.UpdateStatusAsync(doc.Id, LeafletDocumentStatus.Indexed, indexedAt, ct);

            return new IndexLeafletResponse
            {
                DocumentId = doc.Id,
                WasDuplicate = false,
                ChunkCount = chunkCount,
                Status = LeafletDocumentStatus.Indexed,
                Filename = doc.Filename,
                ContentType = doc.ContentType,
                IngestedAt = doc.IngestedAt,
                IndexedAt = indexedAt,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to index leaflet document {Filename}", doc.Filename);
            try
            {
                await _repo.UpdateStatusAsync(doc.Id, LeafletDocumentStatus.Failed, null, ct);
            }
            catch (Exception saveEx)
            {
                _logger.LogError(saveEx, "Failed to persist Failed status for document {Filename}", doc.Filename);
            }
            throw;
        }
    }

    private static string ComputeHash(byte[] content)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = sha.ComputeHash(content);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
