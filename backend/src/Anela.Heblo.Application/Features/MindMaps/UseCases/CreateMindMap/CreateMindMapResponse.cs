using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.CreateMindMap;

public class CreateMindMapResponse : BaseResponse
{
    public Guid Id { get; set; }

    public CreateMindMapResponse() { }
    public CreateMindMapResponse(ErrorCodes errorCode) : base(errorCode) { }
}
