using Anela.Heblo.Domain.Features.KnowledgeBase;
using MediatR;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.GetDocumentContentTypes;

public class GetDocumentContentTypesHandler : IRequestHandler<GetDocumentContentTypesRequest, GetDocumentContentTypesResponse>
{
    private readonly IKnowledgeBaseRepository _repository;

    public GetDocumentContentTypesHandler(IKnowledgeBaseRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetDocumentContentTypesResponse> Handle(
        GetDocumentContentTypesRequest request,
        CancellationToken cancellationToken)
    {
        var contentTypes = await _repository.GetDistinctContentTypesAsync(cancellationToken);

        return new GetDocumentContentTypesResponse
        {
            ContentTypes = contentTypes,
        };
    }
}
