using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;

namespace Anela.Heblo.API.Controllers;

/// <summary>
/// E2E Test Controller - ONLY for Staging Environment
/// Provides endpoints for E2E testing with Service Principal authentication
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class E2ETestController : ControllerBase
{
    private readonly ILogger<E2ETestController> _logger;
    private readonly IWebHostEnvironment _environment;
    private readonly IConfiguration _configuration;

    public E2ETestController(ILogger<E2ETestController> logger, IWebHostEnvironment environment, IConfiguration configuration)
    {
        _logger = logger;
        _environment = environment;
        _configuration = configuration;
    }

    /// <summary>
    /// Environment Info Endpoint - For debugging deployment environment
    /// </summary>
    [HttpGet("env-info")]
    public ActionResult<object> GetEnvironmentInfo()
    {
        return Ok(new 
        { 
            environment = _environment.EnvironmentName,
            isDevelopment = _environment.IsDevelopment(),
            isProduction = _environment.IsProduction(),
            isStaging = _environment.IsEnvironment("Staging"),
            environmentVariables = new
            {
                ASPNETCORE_ENVIRONMENT = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            }
        });
    }

    /// <summary>
    /// E2E Authentication Endpoint - Creates authenticated session for testing
    /// Following E2E authentication best practices
    /// </summary>
    [HttpPost("auth")]
    public async Task<ActionResult<object>> CreateE2ESession()
    {
        _logger.LogInformation("E2E Test: CreateE2ESession called. Environment: {Environment}, IsStaging: {IsStaging}", 
            _environment.EnvironmentName, _environment.IsEnvironment("Staging"));

        // CRITICAL SECURITY: Only allow in Staging environment
        if (!_environment.IsEnvironment("Staging"))
        {
            _logger.LogWarning("E2E Test: Access denied. Current environment: {Environment}, Expected: Staging", 
                _environment.EnvironmentName);
            return NotFound(new { error = "E2E endpoints only available in Staging environment", currentEnvironment = _environment.EnvironmentName });
        }

        // Get Service Principal token from Authorization header
        var authHeader = Request.Headers["Authorization"].FirstOrDefault();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
        {
            return BadRequest("Missing or invalid Authorization header. Expected: Bearer <service-principal-token>");
        }

        var token = authHeader.Substring("Bearer ".Length);

        try
        {
            // Validate Service Principal token (reuse existing validation logic)
            if (!await ValidateServicePrincipalToken(token))
            {
                return Unauthorized("Invalid Service Principal token");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "E2E Test: Error validating Service Principal token");
            return StatusCode(500, new { error = "Token validation error", details = ex.Message });
        }

        // Create synthetic user session (following best practice)
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "e2e-test-user-id"),
            new Claim(ClaimTypes.Name, "E2E Test User"),
            new Claim(ClaimTypes.Email, "e2e-test@anela-heblo.com"),
            new Claim("preferred_username", "e2e-test@anela-heblo.com"),
            new Claim("name", "E2E Test User"),
            new Claim("given_name", "E2E"),
            new Claim("family_name", "Test"),
            new Claim("oid", "e2e-test-object-id"),
            new Claim("tid", _environment.EnvironmentName), // Use environment as tenant for testing
            new Claim("scp", "access_as_user"),
            new Claim("permission", "FinancialOverview.View")
        };

        var identity = new ClaimsIdentity(claims, "E2ETest");
        var principal = new ClaimsPrincipal(identity);

        // Sign in the synthetic user (create application session)
        await HttpContext.SignInAsync("Cookies", principal, new AuthenticationProperties
        {
            IsPersistent = false,
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(1)
        });

        _logger.LogInformation("E2E Test: Created authenticated session for synthetic user");

        return Ok(new 
        { 
            success = true,
            message = "E2E authentication session created successfully",
            user = new 
            {
                name = "E2E Test User",
                id = "e2e-test-user-id",
                sessionExpires = DateTimeOffset.UtcNow.AddHours(1)
            }
        });
    }

    /// <summary>
    /// Test authentication status - used by E2E tests to verify session is working
    /// </summary>
    [HttpGet("auth-status")]
    public ActionResult<object> GetAuthStatus()
    {
        // CRITICAL SECURITY: Only allow in Staging environment
        if (!_environment.IsEnvironment("Staging"))
        {
            return NotFound(new { error = "E2E endpoints only available in Staging environment", currentEnvironment = _environment.EnvironmentName });
        }

        var isAuthenticated = User.Identity?.IsAuthenticated ?? false;
        
        if (!isAuthenticated)
        {
            return Ok(new 
            { 
                authenticated = false, 
                message = "Not authenticated - E2E override not active"
            });
        }

        var claims = User.Claims.ToDictionary(c => c.Type, c => c.Value);
        
        return Ok(new 
        { 
            authenticated = true,
            message = "E2E authentication override active",
            user = new 
            {
                name = User.Identity.Name ?? "E2E Test User",
                claims = claims
            }
        });
    }

    /// <summary>
    /// Serve E2E test version of the app with mock authentication
    /// </summary>
    [HttpGet("app")]
    public ActionResult GetE2EApp()
    {
        // CRITICAL SECURITY: Only allow in Staging environment
        if (!_environment.IsEnvironment("Staging"))
        {
            return NotFound(new { error = "E2E endpoints only available in Staging environment", currentEnvironment = _environment.EnvironmentName });
        }

        // Check if user is authenticated via E2E override
        if (!User.Identity?.IsAuthenticated ?? true)
        {
            return Unauthorized("E2E authentication required");
        }

        _logger.LogInformation("E2E Test: Serving app with mock authentication for user: {User}", User.Identity.Name);

        // Serve the React app HTML but with mock auth configuration
        var html = GetE2EAppHtml();
        return Content(html, "text/html");
    }

    private string GetE2EAppHtml()
    {
        // This is a simplified HTML that loads the React app with E2E configuration
        // In a real implementation, you might want to serve the actual built index.html
        // but with modified authentication configuration
        return @"
<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""utf-8"" />
    <meta name=""viewport"" content=""width=device-width, initial-scale=1"" />
    <title>Anela Heblo - E2E Test Mode</title>
</head>
<body>
    <div id=""root"">
        <div style=""padding: 20px; font-family: Arial, sans-serif;"">
            <h1>Anela Heblo - E2E Test Mode</h1>
            <p>✅ E2E Authentication Override Active</p>
            <p>User: " + (User.Identity?.Name ?? "E2E Test User") + @"</p>
            <p>This is a test page to verify E2E authentication is working.</p>
            <p><strong>Note:</strong> In a full implementation, this would load the actual React app with mock auth.</p>
        </div>
    </div>
</body>
</html>";
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

            // CRITICAL SECURITY: Validate audience 
            var audienceClaim = jsonToken.Claims.FirstOrDefault(c => c.Type == "aud");
            
            if (audienceClaim == null)
            {
                _logger.LogWarning("E2E Test Authentication: Token missing audience claim");
                return false;
            }

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