using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Anela.Heblo.Domain.Features.Configuration;

namespace Anela.Heblo.API.Infrastructure.Authentication;

/// <summary>
/// E2E Test Authentication Middleware - For Staging and Development Environments
/// Allows Service Principal token authentication for automated E2E tests
/// </summary>
public class E2ETestAuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<E2ETestAuthenticationMiddleware> _logger;
    private readonly IWebHostEnvironment _environment;
    private readonly IServicePrincipalTokenValidator _tokenValidator;

    public E2ETestAuthenticationMiddleware(
        RequestDelegate next,
        ILogger<E2ETestAuthenticationMiddleware> logger,
        IWebHostEnvironment environment,
        IServicePrincipalTokenValidator tokenValidator)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
        _tokenValidator = tokenValidator;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // CRITICAL SECURITY: Only allow in Staging and Development environments
        if (!_environment.IsEnvironment("Staging") && !_environment.IsDevelopment())
        {
            await _next(context);
            return;
        }

        // Skip if already authenticated (avoid double authentication)
        if (context.User?.Identity?.IsAuthenticated == true)
        {
            _logger.LogDebug("User already authenticated via primary scheme, skipping E2E middleware");
            await _next(context);
            return;
        }

        // Method 1: Check for E2E session cookie (from E2ETestController)
        var cookieAuthResult = await context.AuthenticateAsync("Cookies");
        if (cookieAuthResult.Succeeded && cookieAuthResult.Principal != null)
        {
            _logger.LogInformation("E2E Authentication: User authenticated via E2E session cookies: {User}", 
                cookieAuthResult.Principal.Identity?.Name);
            
            // Set the authenticated principal for the request
            context.User = cookieAuthResult.Principal;
            
            await _next(context);
            return;
        }

        // Method 2: Check for E2E test header (Service Principal token)
        if (!context.Request.Headers.TryGetValue("X-E2E-Test-Token", out var tokenValues))
        {
            await _next(context);
            return;
        }

        var token = tokenValues.FirstOrDefault();
        if (string.IsNullOrEmpty(token))
        {
            await _next(context);
            return;
        }

        _logger.LogInformation("E2E Test Authentication: Validating Service Principal token");

        try
        {
            // Validate the Service Principal token using dedicated validator
            var isValid = await _tokenValidator.ValidateAsync(token);

            if (!isValid)
            {
                _logger.LogWarning("E2E Test Authentication: Invalid Service Principal token");
                context.Response.StatusCode = 401;
                await context.Response.WriteAsync("Invalid E2E test token");
                return;
            }

            _logger.LogInformation("E2E Test Authentication: Valid Service Principal token, enabling mock auth");

            // Create mock authentication for this request using session service
            var sessionService = context.RequestServices.GetRequiredService<IE2ESessionService>();
            var claims = sessionService.CreateSyntheticUserClaims(_environment.EnvironmentName);
            var identity = new ClaimsIdentity(claims, "E2ETest");
            var principal = new ClaimsPrincipal(identity);

            // Set the user for this request
            context.User = principal;

            // Set a cookie to signal frontend to skip MSAL authentication
            context.Response.Cookies.Append("E2E-Auth-Override", "true", new CookieOptions
            {
                HttpOnly = false, // Frontend needs to read this
                Secure = false, // Allow HTTP in development
                SameSite = SameSiteMode.Lax, // Allow cross-origin requests
                MaxAge = TimeSpan.FromMinutes(30)
            });

            _logger.LogInformation("E2E Test Authentication: Mock user authenticated successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "E2E Test Authentication: Error validating Service Principal token");
            context.Response.StatusCode = 500;
            await context.Response.WriteAsync("Error validating E2E test token");
            return;
        }

        await _next(context);
    }

}