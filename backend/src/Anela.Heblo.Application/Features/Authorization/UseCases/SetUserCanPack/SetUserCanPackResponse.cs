using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.SetUserCanPack;

public class SetUserCanPackResponse : BaseResponse
{
    public SetUserCanPackResponse() { }
    public SetUserCanPackResponse(ErrorCodes errorCode) : base(errorCode) { }
}
