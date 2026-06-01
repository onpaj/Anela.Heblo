using Anela.Heblo.Domain.Features.KnowledgeBase;
using MediatR;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.GetDocuments;

public class GetDocumentsHandler : IRequestHandler<GetDocumentsRequest, GetDocumentsResponse>
{
    private static readonly int[] AllowedPageSizes = [10, 20, 50];
    private static readonly string[] AllowedSortColumns = ["Filename", "Status", "CreatedAt", "IndexedAt"];

    private readonly IKnowledgeBaseRepository _repository;

    public GetDocumentsHandler(IKnowledgeBaseRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetDocumentsResponse> Handle(
        GetDocumentsRequest request,
        CancellationToken cancellationToken)
    {
        var pageNumber = Math.Max(1, request.PageNumber);
        var pageSize = AllowedPageSizes.Contains(request.PageSize) ? request.PageSize : 20;
        var sortBy = AllowedSortColumns.Contains(request.SortBy) ? request.SortBy : "CreatedAt";

        DocumentStatus? statusFilter = null;
        if (!string.IsNullOrEmpty(request.StatusFilter) &&
            Enum.TryParse<DocumentStatus>(request.StatusFilter, ignoreCase: true, out var parsed))
        {
            statusFilter = parsed;
        }

        var (docs, totalCount) = await _repository.GetDocumentsPagedAsync(
            request.FilenameFilter,
            statusFilter,
            request.ContentTypeFilter,
            sortBy,
            request.SortDescending,
            pageNumber,
            pageSize,
            cancellationToken);

        var docIds = docs.Select(d => d.Id).ToList();
        var firstChunkMap = await _repository.GetFirstChunkIdsByDocumentIdsAsync(docIds, cancellationToken);

        return new GetDocumentsResponse
        {
            Documents = docs.Select(d => new DocumentSummary
            {
                Id = d.Id,
                Filename = d.Filename,
                Status = d.Status.ToString().ToLowerInvariant(),
                ContentType = d.ContentType,
                CreatedAt = d.CreatedAt,
                IndexedAt = d.IndexedAt,
                FirstChunkId = firstChunkMap.TryGetValue(d.Id, out var chunkId) ? chunkId : null,
            }).ToList(),
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize,
        };
    }
}
