using Anela.Heblo.Application.Features.Authorization.UseCases;
using Anela.Heblo.Domain.Features.Authorization;
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.GetUsers;

public class GetUsersHandler : IRequestHandler<GetUsersRequest, GetUsersResponse>
{
    private readonly IAuthorizationRepository _repo;
    public GetUsersHandler(IAuthorizationRepository repo) => _repo = repo;

    public async Task<GetUsersResponse> Handle(GetUsersRequest request, CancellationToken ct)
    {
        var users = await _repo.GetAllUsersAsync(ct);
        return new GetUsersResponse
        {
            Users = users.Select(u => new AppUserDto
            {
                Id = u.Id,
                EntraObjectId = u.EntraObjectId,
                Email = u.Email,
                DisplayName = u.DisplayName,
                IsActive = u.IsActive,
                Source = u.Source.ToString(),
                CanPack = u.CanPack,
                LastLoginAt = u.LastLoginAt,
                GroupIds = u.UserGroups.Select(ug => ug.GroupId).ToList(),
            }).OrderBy(u => u.DisplayName).ToList(),
        };
    }
}
