using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.SetUserActive;

public class SetUserActiveResponse : BaseResponse
{
    public SetUserActiveResponse() { }
    public SetUserActiveResponse(ErrorCodes errorCode) : base(errorCode) { }
}
