using Anela.Heblo.Application.Features.MindMaps.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.GetMindMapDetail;

public class GetMindMapDetailResponse : BaseResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string Status { get; set; } = null!;
    public string? LastError { get; set; }
    public string DocumentJson { get; set; } = null!;
    public List<AttachedMeetingDto> Meetings { get; set; } = new();
    public List<MindMapVersionDto> Versions { get; set; } = new();

    public GetMindMapDetailResponse() { }
    public GetMindMapDetailResponse(ErrorCodes errorCode) : base(errorCode) { }
}
