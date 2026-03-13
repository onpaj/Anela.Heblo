using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.GetDocuments;

public class GetDocumentsRequest : IRequest<GetDocumentsResponse>
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string SortBy { get; set; } = "CreatedAt";
    public bool SortDescending { get; set; } = true;
    public string? FilenameFilter { get; set; }
    public string? StatusFilter { get; set; }
    public string? ContentTypeFilter { get; set; }
}

public class GetDocumentsResponse : BaseResponse
{
    public List<DocumentSummary> Documents { get; set; } = [];
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;
}

public class DocumentSummary
{
    public Guid Id { get; set; }
    public string Filename { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? IndexedAt { get; set; }
}
