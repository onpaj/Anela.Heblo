using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.DeleteGroup;

public class DeleteGroupRequest : IRequest<DeleteGroupResponse>
{
    public Guid Id { get; set; }
}
