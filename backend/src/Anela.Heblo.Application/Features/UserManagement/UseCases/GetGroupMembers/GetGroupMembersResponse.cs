using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.UserManagement.UseCases.GetGroupMembers;

public class GetGroupMembersResponse : BaseResponse
{
    public List<UserDto> Members { get; set; } = new();
}