using Anela.Heblo.Domain.Shared.Rag;
using MediatR;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.IndexDocument;

public class IndexDocumentRequest : IRequest<IndexDocumentResponse>
{
    public string Filename { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public byte[] Content { get; set; } = [];

    /// <summary>Document type determined by the source folder. Drives indexing strategy selection.</summary>
    public DocumentType DocumentType { get; set; } = DocumentType.KnowledgeBase;

    public string? DriveId { get; set; }
    public string? GraphItemId { get; set; }
}
