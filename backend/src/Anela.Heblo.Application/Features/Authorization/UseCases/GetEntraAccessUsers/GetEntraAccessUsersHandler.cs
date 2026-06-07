using Anela.Heblo.Application.Features.UserManagement.Services;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.GetEntraAccessUsers;

public class GetEntraAccessUsersHandler : IRequestHandler<GetEntraAccessUsersRequest, GetEntraAccessUsersResponse>
{
    private readonly IGraphService _graphService;

    public GetEntraAccessUsersHandler(IGraphService graphService) => _graphService = graphService;

    public async Task<GetEntraAccessUsersResponse> Handle(GetEntraAccessUsersRequest request, CancellationToken ct)
    {
        var users = await _graphService.GetAppRoleMembersAsync(AccessRoles.Base, ct);
        return new GetEntraAccessUsersResponse
        {
            Users = users.Select(u => new EntraUserDto
            {
                EntraObjectId = u.Id,
                Email = u.Email,
                DisplayName = u.DisplayName,
            }).OrderBy(u => u.DisplayName).ToList(),
        };
    }
}
