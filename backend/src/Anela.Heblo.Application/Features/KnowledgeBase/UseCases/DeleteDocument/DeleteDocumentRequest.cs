using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.DeleteDocument;

public class DeleteDocumentRequest : IRequest<DeleteDocumentResponse>
{
    public Guid DocumentId { get; set; }
}

public class DeleteDocumentResponse : BaseResponse
{
}
