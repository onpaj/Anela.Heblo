using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Dashboard.UseCases.EnableTile;

public class EnableTileResponse : BaseResponse
{
    public EnableTileResponse() : base() { }
    
    public EnableTileResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }
}