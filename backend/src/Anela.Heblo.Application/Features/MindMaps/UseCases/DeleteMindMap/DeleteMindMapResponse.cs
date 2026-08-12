using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.DeleteMindMap;

public class DeleteMindMapResponse : BaseResponse
{
    public DeleteMindMapResponse() { }
    public DeleteMindMapResponse(ErrorCodes errorCode) : base(errorCode) { }
}
