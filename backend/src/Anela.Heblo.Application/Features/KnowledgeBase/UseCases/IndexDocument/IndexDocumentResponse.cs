using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.KnowledgeBase;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.IndexDocument;

public class IndexDocumentResponse : BaseResponse
{
    public Guid DocumentId { get; set; }
    public DocumentStatus Status { get; set; }
    public bool WasDuplicate { get; set; }
    public string Filename { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? IndexedAt { get; set; }
}
