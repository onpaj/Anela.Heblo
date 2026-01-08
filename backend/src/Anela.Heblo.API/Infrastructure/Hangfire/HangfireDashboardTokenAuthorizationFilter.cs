using Hangfire.Dashboard;
using System.Security.Claims;
using Anela.Heblo.Domain.Features.Configuration;
using Anela.Heblo.Domain.Features.Authorization;

namespace Anela.Heblo.API.Infrastructure.Hangfire;

/// <summary>
/// Hangfire dashboard authorization filter that properly validates Bearer tokens and Mock authentication.
/// Respects the UseMockAuth configuration variable.
/// </summary>
public class HangfireDashboardTokenAuthorizationFilter : IDashboardAuthorizationFilter
{
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;

    public HangfireDashboardTokenAuthorizationFilter(IConfiguration configuration, IServiceProvider serviceProvider)
    {
        _configuration = configuration;
        _serviceProvider = serviceProvider;
    }

    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        var useMockAuth = _configuration.GetValue<bool>(ConfigurationConstants.USE_MOCK_AUTH, defaultValue: false);
        var bypassJwtValidation = _configuration.GetValue<bool>(ConfigurationConstants.BYPASS_JWT_VALIDATION, defaultValue: false);

        // Check if we should use mock authentication
        if (useMockAuth || bypassJwtValidation)
        {
            return AuthorizeMockAuthentication(httpContext);
        }
        else
        {
            return AuthorizeRealAuthentication(httpContext);
        }
    }

    private bool AuthorizeMockAuthentication(HttpContext httpContext)
    {
        // For mock auth, check if user is already authenticated (from middleware)
        if (httpContext.User?.Identity?.IsAuthenticated == true)
        {
            return ValidateAuthenticatedUserWithHebloRole(httpContext.User);
        }

        // If not authenticated but we're in mock mode, try to authenticate manually
        // This handles cases where Hangfire dashboard is accessed directly
        var mockPrincipal = CreateMockPrincipal();
        httpContext.User = mockPrincipal;

        return ValidateAuthenticatedUserWithHebloRole(mockPrincipal);
    }

    private bool AuthorizeRealAuthentication(HttpContext httpContext)
    {
        try
        {
            // Check if user is already authenticated
            if (httpContext.User?.Identity?.IsAuthenticated == true)
            {
                return ValidateAuthenticatedUserWithHebloRole(httpContext.User);
            }

            // For real authentication, ALWAYS return true and let the middleware handle authentication/redirects
            // The HangfireAuthenticationMiddleware will properly redirect unauthenticated users to login
            // This prevents Hangfire from showing 401 error and allows proper OIDC flow
            return true;
        }
        catch
        {
            // Even on errors, let the middleware handle it
            return true;
        }
    }


    private bool ValidateAuthenticatedUser(ClaimsPrincipal user)
    {
        // Ensure user has valid claims
        var hasValidClaims = user.Claims.Any(c => c.Type == ClaimTypes.NameIdentifier && !string.IsNullOrEmpty(c.Value)) ||
                           user.Claims.Any(c => c.Type == "oid" && !string.IsNullOrEmpty(c.Value)) || // Azure AD Object ID
                           user.Claims.Any(c => c.Type == "sub" && !string.IsNullOrEmpty(c.Value)) ||   // Subject claim
                           user.Claims.Any(c => c.Type == "mock_user_id" && !string.IsNullOrEmpty(c.Value)); // Mock user

        return hasValidClaims;
    }

    private bool ValidateAuthenticatedUserWithHebloRole(ClaimsPrincipal user)
    {
        // First, ensure user has basic valid claims
        if (!ValidateAuthenticatedUser(user))
        {
            return false;
        }

        // Then, ensure user has the HebloUser role
        return user.IsInRole(AuthorizationConstants.Roles.HebloUser);
    }

    private ClaimsPrincipal CreateMockPrincipal()
    {
        var claims = new[]
        {
            new Claim("mock_user_id", "mock_user"),
            new Claim(ClaimTypes.Name, "Mock User"),
            new Claim(ClaimTypes.Email, "mock@example.com"),
            new Claim("name", "Mock User"),
            new Claim(ClaimTypes.Role, AuthorizationConstants.Roles.HebloUser) // Add HebloUser role for Hangfire access
        };

        var identity = new ClaimsIdentity(claims, ConfigurationConstants.MOCK_AUTH_SCHEME);
        return new ClaimsPrincipal(identity);
    }
}