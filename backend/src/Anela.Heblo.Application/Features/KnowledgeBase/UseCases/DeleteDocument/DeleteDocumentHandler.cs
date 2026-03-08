using Anela.Heblo.Domain.Features.KnowledgeBase;
using MediatR;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.DeleteDocument;

public class DeleteDocumentHandler : IRequestHandler<DeleteDocumentRequest, DeleteDocumentResponse>
{
    private readonly IKnowledgeBaseRepository _repository;

    public DeleteDocumentHandler(IKnowledgeBaseRepository repository)
    {
        _repository = repository;
    }

    public async Task<DeleteDocumentResponse> Handle(
        DeleteDocumentRequest request,
        CancellationToken cancellationToken)
    {
        await _repository.DeleteDocumentAsync(request.DocumentId, cancellationToken);
        return new DeleteDocumentResponse();
    }
}
