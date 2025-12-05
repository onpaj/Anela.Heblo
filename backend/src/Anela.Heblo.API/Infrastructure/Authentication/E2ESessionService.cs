using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Anela.Heblo.Domain.Features.Authorization;

namespace Anela.Heblo.API.Infrastructure.Authentication;

/// <summary>
/// Service for managing E2E test authentication sessions
/// Handles synthetic user session creation and authentication cookie management
/// </summary>
public interface IE2ESessionService
{
    /// <summary>
    /// Creates an E2E authentication session with synthetic user claims
    /// </summary>
    /// <param name="httpContext">The HTTP context to sign in the user</param>
    /// <param name="environmentName">The environment name to use as tenant</param>
    /// <returns>Task representing the async operation</returns>
    Task CreateE2EAuthenticationSessionAsync(HttpContext httpContext, string environmentName);

    /// <summary>
    /// Creates synthetic user claims for E2E testing
    /// </summary>
    /// <param name="environmentName">The environment name to use as tenant</param>
    /// <returns>Array of claims representing the synthetic user</returns>
    Claim[] CreateSyntheticUserClaims(string environmentName);
}

/// <summary>
/// Implementation of E2E session management service
/// Provides synthetic user session creation for E2E testing scenarios
/// </summary>
public class E2ESessionService : IE2ESessionService
{
    private readonly ILogger<E2ESessionService> _logger;

    public E2ESessionService(ILogger<E2ESessionService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Creates an E2E authentication session with synthetic user claims
    /// Signs in the synthetic user using cookie authentication scheme
    /// </summary>
    public async Task CreateE2EAuthenticationSessionAsync(HttpContext httpContext, string environmentName)
    {
        _logger.LogInformation("E2E Session: Creating authenticated session for synthetic user");

        // Create synthetic user claims
        var claims = CreateSyntheticUserClaims(environmentName);
        var identity = new ClaimsIdentity(claims, "E2ETest");
        var principal = new ClaimsPrincipal(identity);

        // Sign in the synthetic user using the cookie authentication scheme
        // This ensures compatibility with the E2E test session management
        await httpContext.SignInAsync("E2ETestCookies", principal, new AuthenticationProperties
        {
            IsPersistent = false,
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1)
        });

        _logger.LogInformation("E2E Session: Created authenticated session for synthetic user");
    }

    /// <summary>
    /// Creates comprehensive synthetic user claims for E2E testing
    /// Includes all necessary claims for application functionality
    /// </summary>
    public Claim[] CreateSyntheticUserClaims(string environmentName)
    {
        _logger.LogDebug("E2E Session: Creating synthetic user claims for environment: {Environment}", environmentName);

        return new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "e2e-test-user-id"),
            new Claim(ClaimTypes.Name, "E2E Test User"),
            new Claim(ClaimTypes.Email, "e2e-test@anela-heblo.com"),
            new Claim("preferred_username", "e2e-test@anela-heblo.com"),
            new Claim("name", "E2E Test User"),
            new Claim("given_name", "E2E"),
            new Claim("family_name", "Test"),
            new Claim("oid", "e2e-test-object-id"),
            new Claim("tid", environmentName), // Use environment as tenant for testing
            new Claim(ClaimTypes.Role, AuthorizationConstants.Roles.HebloUser), // Base role for application access
            new Claim("scp", "access_as_user"),
            new Claim("permission", "FinancialOverview.View")
        };
    }
}