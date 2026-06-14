using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.DeleteGroup;

public class DeleteGroupResponse : BaseResponse
{
    public DeleteGroupResponse() { }
    public DeleteGroupResponse(ErrorCodes errorCode) : base(errorCode) { }
}
