using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.UpdateGroup;

public class UpdateGroupResponse : BaseResponse
{
    public UpdateGroupResponse() { }
    public UpdateGroupResponse(ErrorCodes errorCode) : base(errorCode) { }
}
