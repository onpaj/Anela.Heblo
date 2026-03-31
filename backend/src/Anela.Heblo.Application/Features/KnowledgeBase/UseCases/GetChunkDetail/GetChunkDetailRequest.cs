using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using MediatR;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.GetChunkDetail;

public class GetChunkDetailRequest : IRequest<GetChunkDetailResponse>
{
    public Guid ChunkId { get; set; }
}

public class GetChunkDetailResponse : BaseResponse
{
    public Guid ChunkId { get; set; }
    public Guid DocumentId { get; set; }
    public string Filename { get; set; } = string.Empty;
    public DocumentType DocumentType { get; set; }
    public DateTime? IndexedAt { get; set; }
    public int ChunkIndex { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;

    public GetChunkDetailResponse() { }

    public GetChunkDetailResponse(ErrorCodes errorCode) : base(errorCode) { }
}
