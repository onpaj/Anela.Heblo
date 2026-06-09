using Anela.Heblo.Application.Features.Authorization.UseCases;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.CreateLocalUser;

public class CreateLocalUserResponse : BaseResponse
{
    public AppUserDto? User { get; set; }

    public CreateLocalUserResponse() { }
    public CreateLocalUserResponse(ErrorCodes errorCode) : base(errorCode) { }
}
