using MediatR;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.DeleteDocument;

public class DeleteDocumentRequest : IRequest
{
    public Guid DocumentId { get; set; }
}
