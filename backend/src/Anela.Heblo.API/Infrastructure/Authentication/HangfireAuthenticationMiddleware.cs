using Anela.Heblo.Domain.Features.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.Extensions;

namespace Anela.Heblo.API.Infrastructure.Authentication;

/// <summary>
/// Middleware that ensures users are authenticated before accessing Hangfire dashboard.
/// Handles redirects to authentication for non-authenticated users.
/// </summary>
public class HangfireAuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;

    public HangfireAuthenticationMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _configuration = configuration;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only apply to Hangfire dashboard paths
        if (!context.Request.Path.StartsWithSegments("/hangfire"))
        {
            await _next(context);
            return;
        }

        // Skip authentication middleware if this is part of the authentication process
        // This prevents infinite loops during OIDC callback or authentication flows
        if (IsAuthenticationRequest(context))
        {
            await _next(context);
            return;
        }

        var useMockAuth = _configuration.GetValue<bool>(ConfigurationConstants.USE_MOCK_AUTH, defaultValue: false);
        var bypassJwtValidation = _configuration.GetValue<bool>(ConfigurationConstants.BYPASS_JWT_VALIDATION, defaultValue: false);

        // Check if we should use mock authentication
        if (useMockAuth || bypassJwtValidation)
        {
            // For mock auth, proceed (the filter will handle creating mock user)
            await _next(context);
            return;
        }

        // For real authentication, check if user is authenticated
        if (context.User?.Identity?.IsAuthenticated != true)
        {
            // User is not authenticated, redirect to login
            await HandleUnauthenticatedRequest(context);
            return;
        }

        // User is authenticated, proceed to Hangfire dashboard
        await _next(context);
    }

    private bool IsAuthenticationRequest(HttpContext context)
    {
        // Check if this request is part of the authentication flow
        var path = context.Request.Path.Value?.ToLowerInvariant();
        
        // Skip middleware for OIDC authentication paths
        if (path != null && (
            path.Contains("/signin-oidc") ||
            path.Contains("/signout") ||
            path.Contains("/.well-known") ||
            path.Contains("/oauth") ||
            path.Contains("/connect/token")))
        {
            return true;
        }

        // Skip if request has OAuth parameters (callback from authentication provider)
        if (context.Request.Query.ContainsKey("code") || 
            context.Request.Query.ContainsKey("state") ||
            context.Request.Query.ContainsKey("session_state"))
        {
            return true;
        }

        // Skip if Bearer token is present
        if (context.Request.Headers.ContainsKey("Authorization"))
        {
            return true;
        }

        // Skip if this looks like a callback with authentication cookies already set
        var authCookie = context.Request.Cookies.Keys.FirstOrDefault(k => 
            k.Contains("AspNetCore") || k.Contains("OpenIdConnect") || k.Contains("Correlation"));
        if (!string.IsNullOrEmpty(authCookie))
        {
            return true;
        }

        return false;
    }

    private async Task HandleUnauthenticatedRequest(HttpContext context)
    {
        // Check if this is an API request
        var isApiRequest = context.Request.Headers.Accept.Any(h => 
            h != null && h.Contains("application/json", StringComparison.OrdinalIgnoreCase));

        if (isApiRequest)
        {
            // For API requests, return 401 with WWW-Authenticate header
            context.Response.StatusCode = 401;
            context.Response.Headers.Append("WWW-Authenticate", "Bearer");
            await context.Response.WriteAsync("Authentication required");
            return;
        }

        // Check for potential infinite loop markers
        var referer = context.Request.Headers.Referer.FirstOrDefault();
        var hasAuthAttemptCookie = context.Request.Cookies.ContainsKey("HangfireAuthAttempt");

        // If we detect potential loop conditions, return error instead of redirect
        if (hasAuthAttemptCookie || (!string.IsNullOrEmpty(referer) && referer.Contains("/hangfire")))
        {
            // Clear the auth attempt cookie
            context.Response.Cookies.Delete("HangfireAuthAttempt");
            
            // Return error page instead of redirecting
            context.Response.StatusCode = 403;
            await context.Response.WriteAsync(@"
<!DOCTYPE html>
<html>
<head><title>Authentication Required</title></head>
<body>
    <h1>Authentication Required</h1>
    <p>Please log in to access the Hangfire dashboard.</p>
    <p><a href='/hangfire'>Try again</a> | <a href='/'>Go to Home</a></p>
</body>
</html>");
            return;
        }

        // Set a cookie to track authentication attempts
        context.Response.Cookies.Append("HangfireAuthAttempt", "true", new CookieOptions
        {
            MaxAge = TimeSpan.FromMinutes(5),
            HttpOnly = true,
            Secure = context.Request.IsHttps
        });

        // Standard challenge for authentication
        await context.ChallengeAsync("OpenIdConnect", new AuthenticationProperties
        {
            RedirectUri = context.Request.GetDisplayUrl()
        });
    }
}