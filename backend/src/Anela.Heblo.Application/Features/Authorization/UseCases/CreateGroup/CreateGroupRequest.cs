using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.CreateGroup;

public class CreateGroupRequest : IRequest<CreateGroupResponse>
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public List<string> Permissions { get; set; } = new();
    public List<Guid> ParentGroupIds { get; set; } = new();
}
