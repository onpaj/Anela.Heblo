using Anela.Heblo.Application.Features.Leaflet.Contracts;
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletDocuments;

public class GetLeafletDocumentsRequest : IRequest<GetLeafletDocumentsResponse>
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string SortBy { get; set; } = "IngestedAt";
    public bool SortDescending { get; set; } = true;
    public string? FilenameFilter { get; set; }
    public string? StatusFilter { get; set; }
    public string? ContentTypeFilter { get; set; }
}

public class GetLeafletDocumentsResponse : BaseResponse
{
    public List<LeafletDocumentSummary> Documents { get; set; } = [];
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;
}
