using Anela.Heblo.Application.Features.Authorization.UseCases;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.GetGroupDetail;

public class GetGroupDetailHandler : IRequestHandler<GetGroupDetailRequest, GetGroupDetailResponse>
{
    private readonly IAuthorizationRepository _repo;
    public GetGroupDetailHandler(IAuthorizationRepository repo) => _repo = repo;

    public async Task<GetGroupDetailResponse> Handle(GetGroupDetailRequest request, CancellationToken ct)
    {
        var group = await _repo.GetGroupByIdAsync(request.Id, ct);
        if (group is null)
            return new GetGroupDetailResponse(ErrorCodes.AuthorizationGroupNotFound);

        return new GetGroupDetailResponse
        {
            Group = new GroupDetailDto
            {
                Id = group.Id,
                Name = group.Name,
                Description = group.Description,
                Permissions = group.Permissions.Select(p => p.PermissionValue).OrderBy(v => v).ToList(),
                ParentGroupIds = group.Parents.Select(p => p.ParentGroupId).ToList(),
            },
        };
    }
}
