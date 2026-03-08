using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.GetDocuments;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.UploadDocument;

public class UploadDocumentResponse
{
    public bool Success { get; set; }
    public DocumentSummary? Document { get; set; }
}
