using Anela.Heblo.Domain.Features.Leaflet;
using MediatR;

namespace Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletDocuments;

public class GetLeafletDocumentsHandler : IRequestHandler<GetLeafletDocumentsRequest, GetLeafletDocumentsResponse>
{
    private static readonly int[] AllowedPageSizes = [10, 20, 50];
    private static readonly string[] AllowedSortColumns = ["Filename", "Status", "IngestedAt", "IndexedAt"];

    private readonly ILeafletDocumentRepository _leafletRepository;

    public GetLeafletDocumentsHandler(ILeafletDocumentRepository leafletRepository)
    {
        _leafletRepository = leafletRepository;
    }

    public async Task<GetLeafletDocumentsResponse> Handle(
        GetLeafletDocumentsRequest request,
        CancellationToken cancellationToken)
    {
        var pageNumber = Math.Max(1, request.PageNumber);
        var pageSize = AllowedPageSizes.Contains(request.PageSize) ? request.PageSize : 20;
        var sortBy = AllowedSortColumns.Contains(request.SortBy) ? request.SortBy : "IngestedAt";

        LeafletDocumentStatus? statusFilter = null;
        if (!string.IsNullOrEmpty(request.StatusFilter) &&
            Enum.TryParse<LeafletDocumentStatus>(request.StatusFilter, ignoreCase: true, out var parsed))
        {
            statusFilter = parsed;
        }

        var (docs, totalCount) = await _leafletRepository.GetDocumentsPagedAsync(
            pageNumber,
            pageSize,
            sortBy,
            request.SortDescending,
            request.FilenameFilter,
            statusFilter,
            request.ContentTypeFilter,
            cancellationToken);

        var docIds = docs.Select(d => d.Id).ToList();
        var firstChunkMap = docIds.Count > 0
            ? await _leafletRepository.GetFirstChunkIdsByDocumentIdsAsync(docIds, cancellationToken)
            : new Dictionary<Guid, Guid>();

        return new GetLeafletDocumentsResponse
        {
            Documents = docs.Select(d => new LeafletDocumentSummary
            {
                Id = d.Id,
                Filename = d.Filename,
                Status = d.Status.ToString().ToLowerInvariant(),
                ContentType = d.ContentType,
                IngestedAt = d.IngestedAt,
                IndexedAt = d.IndexedAt,
                FirstChunkId = firstChunkMap.TryGetValue(d.Id, out var chunkId) ? chunkId : null,
            }).ToList(),
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize,
        };
    }
}
