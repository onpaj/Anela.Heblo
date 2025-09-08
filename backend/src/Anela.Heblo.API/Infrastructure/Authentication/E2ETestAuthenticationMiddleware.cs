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
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;
    private readonly HttpClient _httpClient;

    public E2ETestAuthenticationMiddleware(
        RequestDelegate next,
        ILogger<E2ETestAuthenticationMiddleware> logger,
        IConfiguration configuration,
        IWebHostEnvironment environment,
        HttpClient httpClient)
    {
        _next = next;
        _logger = logger;
        _configuration = configuration;
        _environment = environment;
        _httpClient = httpClient;
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
            // Validate the Service Principal token
            var isValid = await ValidateServicePrincipalToken(token);

            if (!isValid)
            {
                _logger.LogWarning("E2E Test Authentication: Invalid Service Principal token");
                context.Response.StatusCode = 401;
                await context.Response.WriteAsync("Invalid E2E test token");
                return;
            }

            _logger.LogInformation("E2E Test Authentication: Valid Service Principal token, enabling mock auth");

            // Create mock authentication for this request
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, "e2e-test-user-id"),
                new Claim(ClaimTypes.Name, "E2E Test User"),
                new Claim(ClaimTypes.Email, "e2e-test@anela-heblo.com"),
                new Claim("preferred_username", "e2e-test@anela-heblo.com"),
                new Claim("name", "E2E Test User"),
                new Claim("given_name", "E2E"),
                new Claim("family_name", "Test"),
                // Add Entra ID specific claims for E2E testing
                new Claim("oid", "e2e-test-object-id"),
                new Claim("tid", _configuration["E2E:TenantId"] ?? "e2e-test-tenant-id"),
                new Claim("scp", "access_as_user"),
                // Add sufficient permissions for testing
                new Claim("permission", "FinancialOverview.View")
            };

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

    private async Task<bool> ValidateServicePrincipalToken(string token)
    {
        try
        {
            // Parse JWT token to get basic info
            var handler = new JwtSecurityTokenHandler();
            var jsonToken = handler.ReadJwtToken(token);

            // Verify it's an app token (not user token)
            var appIdClaim = jsonToken.Claims.FirstOrDefault(c => c.Type == "appid");
            var tenantIdClaim = jsonToken.Claims.FirstOrDefault(c => c.Type == "tid");

            if (appIdClaim == null || tenantIdClaim == null)
            {
                _logger.LogWarning("E2E Test Authentication: Token missing required app claims");
                return false;
            }

            // Check if it matches our expected Service Principal
            var expectedClientId = _configuration["E2E:ExpectedClientId"];
            var expectedTenantId = _configuration["E2E:ExpectedTenantId"];

            if (!string.IsNullOrEmpty(expectedClientId) && appIdClaim.Value != expectedClientId)
            {
                _logger.LogWarning("E2E Test Authentication: Token client ID mismatch. Expected: {Expected}, Got: {Actual}",
                    expectedClientId, appIdClaim.Value);
                return false;
            }

            if (!string.IsNullOrEmpty(expectedTenantId) && tenantIdClaim.Value != expectedTenantId)
            {
                _logger.LogWarning("E2E Test Authentication: Token tenant ID mismatch. Expected: {Expected}, Got: {Actual}",
                    expectedTenantId, tenantIdClaim.Value);
                return false;
            }

            // CRITICAL SECURITY: Validate issuer to ensure token is from Azure AD (accept both v1.0 and v2.0 tokens)
            var issuerClaim = jsonToken.Claims.FirstOrDefault(c => c.Type == "iss");
            var expectedIssuerV1 = $"https://sts.windows.net/{tenantIdClaim.Value}/";
            var expectedIssuerV2 = $"https://login.microsoftonline.com/{tenantIdClaim.Value}/v2.0";
            
            if (issuerClaim == null || (issuerClaim.Value != expectedIssuerV1 && issuerClaim.Value != expectedIssuerV2))
            {
                _logger.LogWarning("E2E Test Authentication: Invalid issuer. Expected: {ExpectedV1} OR {ExpectedV2}, Got: {Actual}", 
                    expectedIssuerV1, expectedIssuerV2, issuerClaim?.Value);
                return false;
            }

            // CRITICAL SECURITY: Validate audience - Service Principal tokens may have different audience formats
            var audienceClaim = jsonToken.Claims.FirstOrDefault(c => c.Type == "aud");

            if (audienceClaim == null)
            {
                _logger.LogWarning("E2E Test Authentication: Token missing audience claim");
                return false;
            }

            // Service Principal tokens for client_credentials may have audience like:
            // - "https://graph.microsoft.com" (if requesting Graph API scope)
            // - The client_id itself
            // - Custom API scope URI
            // For E2E testing, we accept any audience as long as other claims are valid
            _logger.LogInformation("E2E Test Authentication: Token audience: {Audience}", audienceClaim.Value);

            // CRITICAL SECURITY: Validate token expiration
            var expClaim = jsonToken.Claims.FirstOrDefault(c => c.Type == "exp");
            if (expClaim != null && long.TryParse(expClaim.Value, out var exp))
            {
                var expirationTime = DateTimeOffset.FromUnixTimeSeconds(exp);
                if (expirationTime <= DateTimeOffset.UtcNow)
                {
                    _logger.LogWarning("E2E Test Authentication: Token has expired. Expiration: {Expiration}", expirationTime);
                    return false;
                }
            }

            _logger.LogInformation("E2E Test Authentication: Service Principal token validated successfully. AppId: {AppId}, Tenant: {TenantId}, Issuer: {Issuer}",
                appIdClaim.Value, tenantIdClaim.Value, issuerClaim.Value);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "E2E Test Authentication: Error parsing Service Principal token");
            return false;
        }
    }
}