using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.DeleteGroup;

public class DeleteGroupHandler : IRequestHandler<DeleteGroupRequest, DeleteGroupResponse>
{
    private readonly IAuthorizationRepository _repo;
    public DeleteGroupHandler(IAuthorizationRepository repo) => _repo = repo;

    public async Task<DeleteGroupResponse> Handle(DeleteGroupRequest request, CancellationToken ct)
    {
        var group = await _repo.GetGroupByIdAsync(request.Id, ct);
        if (group is null)
            return new DeleteGroupResponse(ErrorCodes.AuthorizationGroupNotFound);
        if (group.IsSystem)
            return new DeleteGroupResponse(ErrorCodes.AuthorizationSystemGroupImmutable);

        await _repo.RemoveGroupAsync(group, ct);
        await _repo.SaveChangesAsync(ct);
        return new DeleteGroupResponse();
    }
}
