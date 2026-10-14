using System.Security.Claims;

namespace Anela.Heblo.API.Infrastructure.Authentication;

/// <summary>Reads which client application obtained the caller's token and whether the token is
/// app-only. Pure functions over the request, shared by CurrentUserService and the /mcp rate limiter.</summary>
public static class TokenClientClaims
{
    public const string AuthorizedPartyClaim = "azp";
    public const string AppIdClaim = "appid";
    public const string IdentityTypeClaim = "idtyp";
    public const string ScopeClaim = "scp";
    public const string MappedScopeClaim = "http://schemas.microsoft.com/identity/claims/scope";

    private const string AppIdentityType = "app";
    private const string BearerPrefix = "Bearer ";

    public static string? GetClientAppId(ClaimsPrincipal? user) =>
        NonBlank(user?.FindFirst(AuthorizedPartyClaim)?.Value)
        ?? NonBlank(user?.FindFirst(AppIdClaim)?.Value);

    public static bool IsBearerRequest(HttpContext? httpContext)
    {
        if (httpContext?.User?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var header = httpContext.Request.Headers.Authorization.ToString();
        return header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsAppOnly(HttpContext? httpContext)
    {
        if (!IsBearerRequest(httpContext))
        {
            return false;
        }

        var user = httpContext!.User;
        var identityType = user.FindFirst(IdentityTypeClaim)?.Value;
        if (string.Equals(identityType, AppIdentityType, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !user.HasClaim(c => c.Type is ScopeClaim or MappedScopeClaim);
    }

    private static string? NonBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
