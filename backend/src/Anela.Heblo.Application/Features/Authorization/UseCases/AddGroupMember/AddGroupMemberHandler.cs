using Anela.Heblo.Application.Features.Authorization.UseCases;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Authorization.Entities;
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.AddGroupMember;

public class AddGroupMemberHandler : IRequestHandler<AddGroupMemberRequest, AddGroupMemberResponse>
{
    private readonly IAuthorizationRepository _repo;
    private readonly IPermissionResolver _resolver;

    public AddGroupMemberHandler(IAuthorizationRepository repo, IPermissionResolver resolver)
    {
        _repo = repo;
        _resolver = resolver;
    }

    public async Task<AddGroupMemberResponse> Handle(AddGroupMemberRequest request, CancellationToken ct)
    {
        var group = await _repo.GetGroupByIdAsync(request.GroupId, ct);
        if (group is null)
            return new AddGroupMemberResponse(ErrorCodes.AuthorizationGroupNotFound);

        var user = await _repo.GetUserByObjectIdAsync(request.EntraObjectId, ct);
        if (user is null)
        {
            user = await _repo.AddUserAsync(new AppUser
            {
                Id = Guid.NewGuid(),
                EntraObjectId = request.EntraObjectId,
                Email = request.Email,
                DisplayName = request.DisplayName,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                LastLoginAt = null,
            }, ct);
        }

        await _repo.AddUserToGroupAsync(user.Id, request.GroupId, ct);
        await _repo.SaveChangesAsync(ct);
        _resolver.InvalidateCache(request.EntraObjectId);

        var groups = await _repo.GetUserGroupsAsync(user.Id, ct);
        return new AddGroupMemberResponse
        {
            User = new AppUserDto
            {
                Id = user.Id,
                EntraObjectId = user.EntraObjectId,
                Email = user.Email,
                DisplayName = user.DisplayName,
                IsActive = user.IsActive,
                LastLoginAt = user.LastLoginAt,
                GroupIds = groups.Select(ug => ug.GroupId).ToList(),
            }
        };
    }
}
