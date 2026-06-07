using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Authorization.Entities;
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.UpdateGroup;

public class UpdateGroupHandler : IRequestHandler<UpdateGroupRequest, UpdateGroupResponse>
{
    private readonly IAuthorizationRepository _repo;
    public UpdateGroupHandler(IAuthorizationRepository repo) => _repo = repo;

    public async Task<UpdateGroupResponse> Handle(UpdateGroupRequest request, CancellationToken ct)
    {
        var group = await _repo.GetGroupByIdAsync(request.Id, ct);
        if (group is null)
            return new UpdateGroupResponse(ErrorCodes.AuthorizationGroupNotFound);
        if (string.IsNullOrWhiteSpace(request.Name))
            return new UpdateGroupResponse(ErrorCodes.ValidationError);

        var trimmedName = request.Name.Trim();
        var duplicate = await _repo.GetGroupByNameAsync(trimmedName, ct);
        if (duplicate is not null && duplicate.Id != group.Id)
            return new UpdateGroupResponse(ErrorCodes.AuthorizationDuplicateGroupName);

        var valid = AccessMatrix.AllRoleValues().ToHashSet();
        if (request.Permissions.Any(p => !valid.Contains(p)))
            return new UpdateGroupResponse(ErrorCodes.AuthorizationInvalidPermission);

        var (_, allParents) = await _repo.GetGroupGraphAsync(ct);
        var existing = allParents.Where(p => p.GroupId != group.Id)
            .GroupBy(p => p.GroupId)
            .ToDictionary(g => g.Key, g => g.Select(p => p.ParentGroupId).ToList());
        if (GroupCycleCheck.WouldCreateCycle(group.Id, request.ParentGroupIds, existing))
            return new UpdateGroupResponse(ErrorCodes.AuthorizationGroupCycleDetected);

        group.Name = trimmedName;
        group.Description = request.Description;

        group.Permissions.Clear();
        foreach (var p in request.Permissions.Distinct())
            group.Permissions.Add(new GroupPermission { GroupId = group.Id, PermissionValue = p });

        group.Parents.Clear();
        foreach (var parentId in request.ParentGroupIds.Distinct())
            group.Parents.Add(new GroupParent { GroupId = group.Id, ParentGroupId = parentId });

        await _repo.SaveChangesAsync(ct);
        return new UpdateGroupResponse();
    }
}
