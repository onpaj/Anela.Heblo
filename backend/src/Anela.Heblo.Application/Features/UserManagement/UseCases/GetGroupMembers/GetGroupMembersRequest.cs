using MediatR;

namespace Anela.Heblo.Application.Features.UserManagement.UseCases.GetGroupMembers;

public class GetGroupMembersRequest : IRequest<GetGroupMembersResponse>
{
    public string GroupId { get; set; } = null!;
}