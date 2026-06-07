using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.AssignUserGroups;

public class AssignUserGroupsRequest : IRequest<AssignUserGroupsResponse>
{
    public Guid UserId { get; set; }
    public List<Guid> GroupIds { get; set; } = new();
}
