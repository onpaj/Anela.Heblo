using Anela.Heblo.Application.Shared.Users.Contracts;
using Anela.Heblo.Domain.Features.Authorization;

namespace Anela.Heblo.Application.Features.Authorization.Infrastructure;

internal sealed class AuthorizationUserDirectorySourceAdapter : IUserDirectorySource
{
    private readonly IAuthorizationRepository _repository;

    public AuthorizationUserDirectorySourceAdapter(IAuthorizationRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<UserDirectoryEntry>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var users = await _repository.GetAllUsersAsync(cancellationToken);

        return users
            .Select(u => new UserDirectoryEntry
            {
                EntraObjectId = u.EntraObjectId,
                Email = u.Email,
                DisplayName = u.DisplayName,
            })
            .ToList();
    }
}
