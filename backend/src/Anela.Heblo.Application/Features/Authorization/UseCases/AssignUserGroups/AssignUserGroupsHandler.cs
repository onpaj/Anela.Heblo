using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.AssignUserGroups;

public class AssignUserGroupsHandler : IRequestHandler<AssignUserGroupsRequest, AssignUserGroupsResponse>
{
    private readonly IAuthorizationRepository _repo;
    private readonly IPermissionResolver _resolver;

    public AssignUserGroupsHandler(IAuthorizationRepository repo, IPermissionResolver resolver)
    {
        _repo = repo;
        _resolver = resolver;
    }

    public async Task<AssignUserGroupsResponse> Handle(AssignUserGroupsRequest request, CancellationToken ct)
    {
        var user = await _repo.GetUserByIdAsync(request.UserId, ct);
        if (user is null)
            return new AssignUserGroupsResponse(ErrorCodes.AuthorizationUserNotFound);

        if (request.GroupIds.Count > 0)
        {
            var allGroups = await _repo.GetAllGroupsAsync(ct);
            var existingIds = allGroups.Select(g => g.Id).ToHashSet();
            if (request.GroupIds.Any(id => !existingIds.Contains(id)))
                return new AssignUserGroupsResponse(ErrorCodes.AuthorizationGroupNotFound);
        }

        await _repo.SetUserGroupsAsync(request.UserId, request.GroupIds, ct);
        await _repo.SaveChangesAsync(ct);
        if (user.EntraObjectId is not null)
            _resolver.InvalidateCache(user.EntraObjectId);
        return new AssignUserGroupsResponse();
    }
}
