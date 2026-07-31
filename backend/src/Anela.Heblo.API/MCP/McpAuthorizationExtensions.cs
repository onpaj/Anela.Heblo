using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.Users;
using ModelContextProtocol;

namespace Anela.Heblo.API.MCP;

public static class McpAuthorizationExtensions
{
    public static void EnsureFeatureAccess(
        this ICurrentUserService currentUserService,
        Feature feature,
        string resourceName,
        AccessLevel level = AccessLevel.Read)
    {
        var requiredRole = AccessRoles.For(feature, level);
        if (!currentUserService.IsInRole(requiredRole))
        {
            throw new McpException(
                $"[FORBIDDEN] You do not have permission to access {resourceName} (requires {requiredRole}).");
        }
    }
}
