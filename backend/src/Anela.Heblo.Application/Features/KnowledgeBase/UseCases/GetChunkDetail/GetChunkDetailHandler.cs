using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using MediatR;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.GetChunkDetail;

public class GetChunkDetailHandler : IRequestHandler<GetChunkDetailRequest, GetChunkDetailResponse>
{
    private readonly IKnowledgeBaseRepository _repository;

    public GetChunkDetailHandler(IKnowledgeBaseRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetChunkDetailResponse> Handle(
        GetChunkDetailRequest request,
        CancellationToken cancellationToken)
    {
        var chunk = await _repository.GetChunkByIdAsync(request.ChunkId, cancellationToken);

        if (chunk is null)
            return new GetChunkDetailResponse(ErrorCodes.KnowledgeBaseChunkNotFound);

        return new GetChunkDetailResponse
        {
            ChunkId = chunk.Id,
            DocumentId = chunk.DocumentId,
            Filename = chunk.Document.Filename,
            DocumentType = chunk.DocumentType,
            IndexedAt = chunk.Document.IndexedAt,
            ChunkIndex = chunk.ChunkIndex,
            Summary = chunk.Summary,
            Content = chunk.Content,
        };
    }
}
