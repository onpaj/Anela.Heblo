using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletDocumentContentTypes;

public class GetLeafletDocumentContentTypesRequest : IRequest<GetLeafletDocumentContentTypesResponse>
{
}

public class GetLeafletDocumentContentTypesResponse : BaseResponse
{
    public IReadOnlyList<string> ContentTypes { get; set; } = [];
}
