using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.GenerateDraftReply;

public class GenerateDraftReplyResponse : BaseResponse
{
    public string Answer { get; set; } = string.Empty;
    public List<DraftReplySource> Sources { get; set; } = new();

    public GenerateDraftReplyResponse() { }
    public GenerateDraftReplyResponse(ErrorCodes errorCode) : base(errorCode) { }
}

/// <summary>Smartsupp-local mirror of a KnowledgeBase source chunk reference.</summary>
public class DraftReplySource
{
    public Guid ChunkId { get; set; }
    public Guid DocumentId { get; set; }
    public string Filename { get; set; } = string.Empty;
    public string Excerpt { get; set; } = string.Empty;
    public double Score { get; set; }
}
