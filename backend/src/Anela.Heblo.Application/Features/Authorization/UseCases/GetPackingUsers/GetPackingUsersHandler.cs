using Anela.Heblo.Domain.Features.Authorization;
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.GetPackingUsers;

public class GetPackingUsersHandler : IRequestHandler<GetPackingUsersRequest, GetPackingUsersResponse>
{
    private readonly IAuthorizationRepository _repo;
    public GetPackingUsersHandler(IAuthorizationRepository repo) => _repo = repo;

    public async Task<GetPackingUsersResponse> Handle(GetPackingUsersRequest request, CancellationToken ct)
    {
        var users = await _repo.GetActivePackingUsersAsync(ct);
        return new GetPackingUsersResponse
        {
            Users = users.Select(u => new PackingUserDto { Id = u.Id, DisplayName = u.DisplayName }).ToList(),
        };
    }
}
