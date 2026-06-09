using System.Security.Claims;
using Anela.Heblo.Domain.Features.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Identity.Web;

namespace Anela.Heblo.API.Infrastructure.Authentication;

/// <summary>Injects a user's effective permissions as Role claims after authentication.
/// super_user (from the token) is a wildcard granting all AccessMatrix permissions.</summary>
public class PermissionClaimsTransformation : IClaimsTransformation
{
    private readonly IPermissionResolver _resolver;

    public PermissionClaimsTransformation(IPermissionResolver resolver) => _resolver = resolver;

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        var identity = principal.Identity as ClaimsIdentity;
        if (identity is null || !identity.IsAuthenticated)
            return principal;

        // Guard against re-running (IClaimsTransformation can be invoked multiple times per request).
        if (identity.HasClaim("authz_applied", "1"))
            return principal;

        // GetObjectId() reads both "oid" (raw) and "http://schemas.microsoft.com/identity/claims/objectidentifier"
        // (the URI the JwtBearer handler renames "oid" to when MapInboundClaims is on).
        // This is the tenant-wide Entra Object ID — same value Graph returns as /users/{id}.id,
        // which is what EntraMemberSearch stores in AppUser.EntraObjectId.
        // NameIdentifier fallback is for mock auth (and any other scheme without an oid claim).
        var objectId = principal.GetObjectId()
                       ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        IReadOnlyCollection<string> permissions;
        if (objectId is not null)
        {
            // Email/name extraction MUST be defensive: Microsoft.Identity.Web's default
            // NameClaimType for Web APIs is "preferred_username", which makes
            // FindFirst(ClaimTypes.Name) silently return the user's UPN/email instead of
            // their display name. Naively picking ClaimTypes.Name → "name" caused the
            // resolver to materialize AppUser rows with DisplayName=UPN and Email=null
            // (then falling back to entraObjectId, producing rows like Email='3983f7e5-…').
            // Order email candidates from most-specific to least-specific Entra/standard.
            var email = principal.FindFirst("preferred_username")?.Value
                        ?? principal.FindFirst("upn")?.Value
                        ?? principal.FindFirst(ClaimTypes.Email)?.Value
                        ?? principal.FindFirst("email")?.Value
                        ?? principal.FindFirst(ClaimTypes.Upn)?.Value;
            // Prefer the raw "name" claim (Entra display name); fall through to
            // ClaimTypes.Name last because in Web API config it usually aliases UPN.
            var name = principal.FindFirst("name")?.Value
                       ?? principal.FindFirst(ClaimTypes.GivenName)?.Value
                       ?? principal.FindFirst(ClaimTypes.Name)?.Value;
            // Always call the resolver: it materializes the AppUser and stamps LastLoginAt.
            // For super_users the returned permissions are ignored in favor of the wildcard,
            // but the login-recording side effect MUST still run.
            var resolved = await _resolver.ResolveAsync(objectId, email, name);
            permissions = principal.IsInRole(AccessRoles.SuperUser)
                ? AccessMatrix.AllRoleValues().Append(AccessRoles.Base).ToArray()
                : resolved.Permissions;
        }
        else if (principal.IsInRole(AccessRoles.SuperUser))
        {
            // Defensive: super_user token without any object identifier (not expected in
            // production Entra or mock auth, but preserve the break-glass grant).
            permissions = AccessMatrix.AllRoleValues().Append(AccessRoles.Base).ToArray();
        }
        else
        {
            permissions = Array.Empty<string>();
        }

        // CRITICAL: add role claims using the identity's RoleClaimType, NOT a hardcoded
        // ClaimTypes.Role. Microsoft.Identity.Web configures Entra to use the "roles" claim
        // type; [Authorize(Roles=…)] / IsInRole check that configured type. Using the wrong
        // type would silently make every role check fail.
        var roleClaimType = identity.RoleClaimType; // e.g. "roles" for Entra, ClaimTypes.Role for mock
        foreach (var perm in permissions)
            if (!identity.HasClaim(roleClaimType, perm))
                identity.AddClaim(new Claim(roleClaimType, perm));

        identity.AddClaim(new Claim("authz_applied", "1"));
        return principal;
    }
}
