using System.Security.Claims;
using Anela.Heblo.Domain.Features.Users;
using Microsoft.AspNetCore.Http;
using Microsoft.Identity.Web;

namespace Anela.Heblo.API.Features.Users;

public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public CurrentUser GetCurrentUser()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        var isAuthenticated = user?.Identity?.IsAuthenticated ?? false;

        var name = user?.Identity?.Name
                   ?? user?.FindFirst(ClaimTypes.Name)?.Value
                   ?? user?.FindFirst("name")?.Value
                   ?? (isAuthenticated ? "Unknown User" : "Anonymous");

        // MUST match PermissionClaimsTransformation's lookup key: the tenant-wide Entra
        // Object ID. EntraMemberSearch stores oid in AppUser.EntraObjectId, and the
        // claims transformation resolves permissions by oid. If GetMe (which calls the
        // resolver with this Id) keyed on NameIdentifier instead, it would find a
        // different AppUser than [Authorize] checks, causing FE/BE permission
        // divergence and unexplained 403s.
        // GetObjectId() reads "oid" (raw JWT) or the URI-mapped form
        // (http://schemas.microsoft.com/identity/claims/objectidentifier).
        // NameIdentifier fallback covers mock auth and schemes without an oid claim.
        var id = user?.GetObjectId()
                 ?? user?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                 ?? user?.FindFirst("sub")?.Value;

        // Entra ID access tokens omit the `email` claim by default; the user's
        // email/UPN lives in `preferred_username` (and sometimes `upn`).
        // ClaimTypes.Upn covers the legacy JwtSecurityTokenHandler claim remap.
        var email = user?.FindFirst(ClaimTypes.Email)?.Value
                    ?? user?.FindFirst("email")?.Value
                    ?? user?.FindFirst("preferred_username")?.Value
                    ?? user?.FindFirst(ClaimTypes.Upn)?.Value
                    ?? user?.FindFirst("upn")?.Value;

        return new CurrentUser(
            Id: id,
            Name: name,
            Email: email,
            IsAuthenticated: isAuthenticated
        );
    }

    public bool IsInRole(string role)
    {
        return _httpContextAccessor.HttpContext?.User?.IsInRole(role) ?? false;
    }
}
