using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.GetDocuments;

public class GetDocumentsRequest : IRequest<GetDocumentsResponse>
{
}

public class GetDocumentsResponse : BaseResponse
{
    public List<DocumentSummary> Documents { get; set; } = [];
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
