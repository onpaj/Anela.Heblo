using Anela.Heblo.Application.Features.Authorization.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.Authorization.UseCases.GetEntraAccessUsers;

public class GetEntraAccessUsersHandler : IRequestHandler<GetEntraAccessUsersRequest, GetEntraAccessUsersResponse>
{
    private readonly IEntraAccessUserSource _source;

    public GetEntraAccessUsersHandler(IEntraAccessUserSource source) => _source = source;

    public async Task<GetEntraAccessUsersResponse> Handle(GetEntraAccessUsersRequest request, CancellationToken ct)
    {
        var users = await _source.GetBaseMembersAsync(ct);
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
