using Anela.Heblo.Application.Features.KnowledgeBase.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.UploadDocument;

public class UploadDocumentResponse : BaseResponse
{
    public DocumentSummary? Document { get; set; }
}
