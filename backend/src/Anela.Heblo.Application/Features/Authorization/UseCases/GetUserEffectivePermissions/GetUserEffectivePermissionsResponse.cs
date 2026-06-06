using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.GetUserEffectivePermissions;

public class GetUserEffectivePermissionsResponse : BaseResponse
{
    public List<string> Permissions { get; set; } = new();
    public GetUserEffectivePermissionsResponse() { }
    public GetUserEffectivePermissionsResponse(ErrorCodes errorCode) : base(errorCode) { }
}
