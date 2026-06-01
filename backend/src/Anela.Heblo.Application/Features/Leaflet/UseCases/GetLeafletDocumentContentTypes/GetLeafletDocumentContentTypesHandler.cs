using Anela.Heblo.Domain.Features.Leaflet;
using MediatR;

namespace Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletDocumentContentTypes;

public class GetLeafletDocumentContentTypesHandler : IRequestHandler<GetLeafletDocumentContentTypesRequest, GetLeafletDocumentContentTypesResponse>
{
    private readonly ILeafletDocumentRepository _leafletRepository;

    public GetLeafletDocumentContentTypesHandler(ILeafletDocumentRepository leafletRepository)
    {
        _leafletRepository = leafletRepository;
    }

    public async Task<GetLeafletDocumentContentTypesResponse> Handle(
        GetLeafletDocumentContentTypesRequest request,
        CancellationToken cancellationToken)
    {
        var contentTypes = await _leafletRepository.GetDistinctContentTypesAsync(cancellationToken);

        return new GetLeafletDocumentContentTypesResponse
        {
            ContentTypes = contentTypes,
        };
    }
}
