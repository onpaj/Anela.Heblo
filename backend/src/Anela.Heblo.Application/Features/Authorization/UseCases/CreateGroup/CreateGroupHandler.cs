using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Authorization.Entities;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.CreateGroup;

public class CreateGroupHandler : IRequestHandler<CreateGroupRequest, CreateGroupResponse>
{
    private readonly IAuthorizationRepository _repo;
    private readonly ICurrentUserService _currentUser;

    public CreateGroupHandler(IAuthorizationRepository repo, ICurrentUserService currentUser)
    {
        _repo = repo;
        _currentUser = currentUser;
    }

    public async Task<CreateGroupResponse> Handle(CreateGroupRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return new CreateGroupResponse(ErrorCodes.ValidationError);

        var valid = AccessMatrix.AllRoleValues().ToHashSet();
        if (request.Permissions.Any(p => !valid.Contains(p)))
            return new CreateGroupResponse(ErrorCodes.AuthorizationInvalidPermission);

        if (await _repo.GetGroupByNameAsync(request.Name, ct) is not null)
            return new CreateGroupResponse(ErrorCodes.AuthorizationDuplicateGroupName);

        var id = Guid.NewGuid();
        if (request.ParentGroupIds.Count > 0)
        {
            var (_, parents) = await _repo.GetGroupGraphAsync(ct);
            var existing = parents.GroupBy(p => p.GroupId)
                .ToDictionary(g => g.Key, g => g.Select(p => p.ParentGroupId).ToList());
            if (GroupCycleCheck.WouldCreateCycle(id, request.ParentGroupIds, existing))
                return new CreateGroupResponse(ErrorCodes.AuthorizationGroupCycleDetected);
        }

        var group = new PermissionGroup
        {
            Id = id,
            Name = request.Name.Trim(),
            Description = request.Description,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = _currentUser.GetCurrentUser().Email,
        };
        foreach (var p in request.Permissions.Distinct())
            group.Permissions.Add(new GroupPermission { GroupId = id, PermissionValue = p });
        foreach (var parentId in request.ParentGroupIds.Distinct())
            group.Parents.Add(new GroupParent { GroupId = id, ParentGroupId = parentId });

        await _repo.AddGroupAsync(group, ct);
        await _repo.SaveChangesAsync(ct);

        return new CreateGroupResponse { Id = id };
    }
}
