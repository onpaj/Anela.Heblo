using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Persistence.Features.Authorization;
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.GetUserEffectivePermissions;

public class GetUserEffectivePermissionsHandler
    : IRequestHandler<GetUserEffectivePermissionsRequest, GetUserEffectivePermissionsResponse>
{
    private readonly IAuthorizationRepository _repo;
    public GetUserEffectivePermissionsHandler(IAuthorizationRepository repo) => _repo = repo;

    public async Task<GetUserEffectivePermissionsResponse> Handle(
        GetUserEffectivePermissionsRequest request, CancellationToken ct)
    {
        var user = await _repo.GetUserByIdAsync(request.UserId, ct);
        if (user is null)
            return new GetUserEffectivePermissionsResponse(ErrorCodes.AuthorizationUserNotFound);

        if (!user.IsActive)
            return new GetUserEffectivePermissionsResponse { Permissions = new() };

        var (perms, parents) = await _repo.GetGroupGraphAsync(ct);
        var groupIds = user.UserGroups.Select(ug => ug.GroupId);
        var resolved = GroupClosure.Resolve(groupIds, perms, parents);
        var all = new HashSet<string>(resolved) { AccessRoles.Base };

        return new GetUserEffectivePermissionsResponse { Permissions = all.OrderBy(p => p).ToList() };
    }
}
