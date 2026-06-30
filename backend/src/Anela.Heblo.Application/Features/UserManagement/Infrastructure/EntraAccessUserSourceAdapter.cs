using Anela.Heblo.Application.Features.Authorization.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Services;
using Anela.Heblo.Domain.Features.Authorization;

namespace Anela.Heblo.Application.Features.UserManagement.Infrastructure;

internal sealed class EntraAccessUserSourceAdapter : IEntraAccessUserSource
{
    private readonly IGraphService _graph;

    public EntraAccessUserSourceAdapter(IGraphService graph) => _graph = graph;

    public async Task<List<EntraAccessUserRecord>> GetBaseMembersAsync(CancellationToken ct)
    {
        var users = await _graph.GetAppRoleMembersAsync(AccessRoles.Base, ct);
        return users
            .Select(u => new EntraAccessUserRecord(u.Id, u.Email, u.DisplayName))
            .ToList();
    }
}
