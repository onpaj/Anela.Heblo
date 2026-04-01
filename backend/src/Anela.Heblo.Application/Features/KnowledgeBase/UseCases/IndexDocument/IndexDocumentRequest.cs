using Anela.Heblo.Domain.Features.KnowledgeBase;
using MediatR;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.IndexDocument;

public class IndexDocumentRequest : IRequest
{
    public string Filename { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public byte[] Content { get; set; } = [];

    /// <summary>SHA-256 hex digest of Content bytes. Computed by the caller (ingestion job) before sending.</summary>
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>Document type determined by the source folder. Drives indexing strategy selection.</summary>
    public DocumentType DocumentType { get; set; } = DocumentType.KnowledgeBase;
}
