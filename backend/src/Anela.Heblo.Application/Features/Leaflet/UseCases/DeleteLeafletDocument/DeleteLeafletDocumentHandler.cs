using Anela.Heblo.Domain.Features.Leaflet;
using MediatR;

namespace Anela.Heblo.Application.Features.Leaflet.UseCases.DeleteLeafletDocument;

public class DeleteLeafletDocumentHandler : IRequestHandler<DeleteLeafletDocumentRequest, DeleteLeafletDocumentResponse>
{
    private readonly ILeafletDocumentRepository _leafletRepository;

    public DeleteLeafletDocumentHandler(ILeafletDocumentRepository leafletRepository)
    {
        _leafletRepository = leafletRepository;
    }

    public async Task<DeleteLeafletDocumentResponse> Handle(
        DeleteLeafletDocumentRequest request,
        CancellationToken cancellationToken)
    {
        await _leafletRepository.DeleteDocumentAsync(request.DocumentId, cancellationToken);
        return new DeleteLeafletDocumentResponse();
    }
}
