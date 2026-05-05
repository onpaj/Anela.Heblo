using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Leaflet.UseCases.DeleteLeafletDocument;

public class DeleteLeafletDocumentRequest : IRequest<DeleteLeafletDocumentResponse>
{
    public Guid DocumentId { get; set; }
}

public class DeleteLeafletDocumentResponse : BaseResponse
{
}
