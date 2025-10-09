using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Dashboard.UseCases.DisableTile;

public class DisableTileResponse : BaseResponse
{
    public DisableTileResponse() : base() { }
    
    public DisableTileResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }
}