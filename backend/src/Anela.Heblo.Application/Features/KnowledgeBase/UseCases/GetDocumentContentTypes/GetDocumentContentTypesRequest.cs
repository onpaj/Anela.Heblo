using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.GetDocumentContentTypes;

public class GetDocumentContentTypesRequest : IRequest<GetDocumentContentTypesResponse>
{
}

public class GetDocumentContentTypesResponse : BaseResponse
{
    public List<string> ContentTypes { get; set; } = [];
}
