using Anela.Heblo.Domain.Features.KnowledgeBase;
using MediatR;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.GetDocuments;

public class GetDocumentsHandler : IRequestHandler<GetDocumentsRequest, GetDocumentsResponse>
{
    private readonly IKnowledgeBaseRepository _repository;

    public GetDocumentsHandler(IKnowledgeBaseRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetDocumentsResponse> Handle(
        GetDocumentsRequest request,
        CancellationToken cancellationToken)
    {
        var docs = await _repository.GetAllDocumentsAsync(cancellationToken);

        return new GetDocumentsResponse
        {
            Documents = docs.Select(d => new DocumentSummary
            {
                Id = d.Id,
                Filename = d.Filename,
                Status = d.Status.ToString().ToLowerInvariant(),
                ContentType = d.ContentType,
                CreatedAt = d.CreatedAt,
                IndexedAt = d.IndexedAt
            }).ToList()
        };
    }
}
