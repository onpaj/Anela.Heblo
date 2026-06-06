using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.AssignUserGroups;

public class AssignUserGroupsHandler : IRequestHandler<AssignUserGroupsRequest, AssignUserGroupsResponse>
{
    private readonly IAuthorizationRepository _repo;
    public AssignUserGroupsHandler(IAuthorizationRepository repo) => _repo = repo;

    public async Task<AssignUserGroupsResponse> Handle(AssignUserGroupsRequest request, CancellationToken ct)
    {
        var user = await _repo.GetUserByIdAsync(request.UserId, ct);
        if (user is null)
            return new AssignUserGroupsResponse(ErrorCodes.AuthorizationUserNotFound);

        await _repo.SetUserGroupsAsync(request.UserId, request.GroupIds, ct);
        await _repo.SaveChangesAsync(ct);
        return new AssignUserGroupsResponse();
    }
}
