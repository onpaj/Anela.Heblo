using Anela.Heblo.Application.Features.Authorization.UseCases;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.GetGroups;

public class GetGroupsHandler : IRequestHandler<GetGroupsRequest, GetGroupsResponse>
{
    private readonly IAuthorizationRepository _repo;
    public GetGroupsHandler(IAuthorizationRepository repo) => _repo = repo;

    public async Task<GetGroupsResponse> Handle(GetGroupsRequest request, CancellationToken ct)
    {
        var groups = await _repo.GetAllGroupsAsync(ct);
        return new GetGroupsResponse
        {
            Groups = groups.Select(g => new GroupSummaryDto
            {
                Id = g.Id,
                Name = g.Name,
                Description = g.Description,
                PermissionCount = g.Permissions.Count,
                ParentCount = g.Parents.Count,
                MemberCount = g.UserGroups.Count,
            }).OrderBy(g => g.Name).ToList(),
        };
    }
}
