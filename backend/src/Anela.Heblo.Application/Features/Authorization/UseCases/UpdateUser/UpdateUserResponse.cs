using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.UpdateUser;

public class UpdateUserResponse : BaseResponse
{
    public UpdateUserResponse() { }
    public UpdateUserResponse(ErrorCodes errorCode) : base(errorCode) { }
}
