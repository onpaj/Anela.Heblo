using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.UpdateGroup;

public class UpdateGroupRequest : IRequest<UpdateGroupResponse>
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public List<string> Permissions { get; set; } = new();
    public List<Guid> ParentGroupIds { get; set; } = new();
}
