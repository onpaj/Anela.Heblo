using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Leaflet;
using MediatR;

namespace Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletChunkDetail;

public class GetLeafletChunkDetailHandler : IRequestHandler<GetLeafletChunkDetailRequest, GetLeafletChunkDetailResponse>
{
    private readonly ILeafletDocumentRepository _leafletRepository;

    public GetLeafletChunkDetailHandler(ILeafletDocumentRepository leafletRepository)
    {
        _leafletRepository = leafletRepository;
    }

    public async Task<GetLeafletChunkDetailResponse> Handle(
        GetLeafletChunkDetailRequest request,
        CancellationToken cancellationToken)
    {
        var chunk = await _leafletRepository.GetChunkByIdAsync(request.ChunkId, cancellationToken);

        if (chunk is null)
            return new GetLeafletChunkDetailResponse(ErrorCodes.LeafletChunkNotFound);

        return new GetLeafletChunkDetailResponse
        {
            ChunkId = chunk.Id,
            DocumentId = chunk.DocumentId,
            Filename = chunk.Document.Filename,
            IndexedAt = chunk.Document.IndexedAt,
            ChunkIndex = chunk.ChunkIndex,
            Content = chunk.Content,
            Summary = chunk.Summary,
            SourcePath = chunk.Document.SourcePath,
        };
    }
}
