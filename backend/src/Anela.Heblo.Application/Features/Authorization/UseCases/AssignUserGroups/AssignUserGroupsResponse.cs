using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.AssignUserGroups;

public class AssignUserGroupsResponse : BaseResponse
{
    public AssignUserGroupsResponse() { }
    public AssignUserGroupsResponse(ErrorCodes errorCode) : base(errorCode) { }
}
