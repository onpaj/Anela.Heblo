using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletChunkDetail;

public class GetLeafletChunkDetailRequest : IRequest<GetLeafletChunkDetailResponse>
{
    public Guid ChunkId { get; set; }
}

public class GetLeafletChunkDetailResponse : BaseResponse
{
    public Guid ChunkId { get; set; }
    public Guid DocumentId { get; set; }
    public string Filename { get; set; } = string.Empty;
    public DateTime? IndexedAt { get; set; }
    public int ChunkIndex { get; set; }
    public string Content { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string? SourcePath { get; set; }

    public GetLeafletChunkDetailResponse() { }

    public GetLeafletChunkDetailResponse(ErrorCodes errorCode) : base(errorCode) { }
}
