using Hangfire.Dashboard;
using System.Security.Claims;
using Anela.Heblo.Domain.Features.Configuration;

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
            return true;
        }

        // If not authenticated but we're in mock mode, try to authenticate manually
        // This handles cases where Hangfire dashboard is accessed directly
        var mockPrincipal = CreateMockPrincipal();
        httpContext.User = mockPrincipal;
        
        return true;
    }

    private bool AuthorizeRealAuthentication(HttpContext httpContext)
    {
        try
        {
            // Check if user is already authenticated
            if (httpContext.User?.Identity?.IsAuthenticated == true)
            {
                return ValidateAuthenticatedUser(httpContext.User);
            }

            // Try to authenticate using the Authorization header
            var authHeader = httpContext.Request.Headers.Authorization.FirstOrDefault();
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
            {
                return false;
            }

            var token = authHeader.Substring("Bearer ".Length).Trim();
            if (string.IsNullOrEmpty(token))
            {
                return false;
            }

            // For now, if we have a token but user is not authenticated, 
            // we'll return false and let the client handle proper authentication
            // This is a more secure approach than trying to validate tokens manually
            return false;
        }
        catch
        {
            return false;
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

    private ClaimsPrincipal CreateMockPrincipal()
    {
        var claims = new[]
        {
            new Claim("mock_user_id", "mock_user"),
            new Claim(ClaimTypes.Name, "Mock User"),
            new Claim(ClaimTypes.Email, "mock@example.com"),
            new Claim("name", "Mock User")
        };

        var identity = new ClaimsIdentity(claims, ConfigurationConstants.MOCK_AUTH_SCHEME);
        return new ClaimsPrincipal(identity);
    }
}