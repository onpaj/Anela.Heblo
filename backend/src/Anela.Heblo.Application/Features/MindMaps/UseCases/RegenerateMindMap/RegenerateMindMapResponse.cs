using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.RegenerateMindMap;

public class RegenerateMindMapResponse : BaseResponse
{
    public RegenerateMindMapResponse() { }
    public RegenerateMindMapResponse(ErrorCodes errorCode) : base(errorCode) { }
}
