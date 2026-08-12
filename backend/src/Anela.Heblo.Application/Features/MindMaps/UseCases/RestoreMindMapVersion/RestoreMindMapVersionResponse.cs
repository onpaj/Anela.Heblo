using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MindMaps.UseCases.RestoreMindMapVersion;

public class RestoreMindMapVersionResponse : BaseResponse
{
    public string DocumentJson { get; set; } = null!;

    public RestoreMindMapVersionResponse() { }
    public RestoreMindMapVersionResponse(ErrorCodes errorCode) : base(errorCode) { }
}
