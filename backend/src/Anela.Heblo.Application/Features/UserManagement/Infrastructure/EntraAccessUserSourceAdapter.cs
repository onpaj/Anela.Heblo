using Anela.Heblo.Application.Features.Authorization.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Services;
using Anela.Heblo.Domain.Features.Authorization;

namespace Anela.Heblo.Application.Features.UserManagement.Infrastructure;

internal sealed class EntraAccessUserSourceAdapter : IEntraAccessUserSource
{
    private readonly IGraphService _graph;

    public EntraAccessUserSourceAdapter(IGraphService graph) => _graph = graph;

    public async Task<List<EntraAccessUserRecord>> GetBaseMembersAsync(CancellationToken ct)
    {
        List<UserDto> users;
        try
        {
            users = await _graph.GetAppRoleMembersAsync(AccessRoles.Base, ct);
        }
        catch (GraphServiceAuthException ex)
        {
            throw new EntraAccessSourceAuthException(
                $"Failed to resolve Entra Base role members: {ex.Message}", ex);
        }
        catch (GraphServiceException ex)
        {
            throw new EntraAccessSourceException(
                $"Failed to resolve Entra Base role members: {ex.Message}", ex);
        }

        return users
            .Select(u => new EntraAccessUserRecord(u.Id, u.Email, u.DisplayName))
            .ToList();
    }
}
