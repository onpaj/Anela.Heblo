using Hangfire.Dashboard;
using System.Security.Claims;

namespace Anela.Heblo.API.Infrastructure.Hangfire;

/// <summary>
/// Hangfire dashboard authorization filter for production environment.
/// Requires authenticated users with valid Microsoft Entra ID credentials.
/// </summary>
public class HangfireDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();

        // Check if user is authenticated
        if (httpContext.User?.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        // For Microsoft Entra ID authentication, check for required claims
        var user = httpContext.User;

        // Ensure user has valid Azure AD claims
        var hasValidClaims = user.Claims.Any(c => c.Type == ClaimTypes.NameIdentifier && !string.IsNullOrEmpty(c.Value)) ||
                           user.Claims.Any(c => c.Type == "oid" && !string.IsNullOrEmpty(c.Value)) || // Azure AD Object ID
                           user.Claims.Any(c => c.Type == "sub" && !string.IsNullOrEmpty(c.Value));   // Subject claim

        if (!hasValidClaims)
        {
            return false;
        }

        // Optional: Add role-based authorization
        // For now, allow any authenticated Azure AD user
        // Future enhancement: Check for specific roles or groups
        // Example: user.IsInRole("HangfireAdministrators")

        return true;
    }
}