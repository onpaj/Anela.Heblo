using Anela.Heblo.Domain.Features.KnowledgeBase;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.DeleteDocument;

public class DeleteDocumentHandler : IRequestHandler<DeleteDocumentRequest>
{
    private readonly IKnowledgeBaseRepository _repository;
    private readonly ILogger<DeleteDocumentHandler> _logger;

    public DeleteDocumentHandler(IKnowledgeBaseRepository repository, ILogger<DeleteDocumentHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task Handle(DeleteDocumentRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Deleting knowledge base document {DocumentId}", request.DocumentId);
        await _repository.DeleteDocumentAsync(request.DocumentId, cancellationToken);
        _logger.LogInformation("Document {DocumentId} deleted", request.DocumentId);
    }
}
