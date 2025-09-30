using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using Anela.Heblo.API.Infrastructure.Authentication;

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
    private readonly IServicePrincipalTokenValidator _tokenValidator;
    private readonly IE2ESessionService _sessionService;

    public E2ETestController(
        ILogger<E2ETestController> logger,
        IWebHostEnvironment environment,
        IConfiguration configuration,
        IServicePrincipalTokenValidator tokenValidator,
        IE2ESessionService sessionService)
    {
        _logger = logger;
        _environment = environment;
        _configuration = configuration;
        _tokenValidator = tokenValidator;
        _sessionService = sessionService;
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
    [AllowAnonymous]
    public async Task<ActionResult<object>> CreateE2ESession()
    {
        _logger.LogInformation("E2E Test: CreateE2ESession called. Environment: {Environment}, IsStaging: {IsStaging}",
            _environment.EnvironmentName, _environment.IsEnvironment("Staging"));

        // CRITICAL SECURITY: Only allow in Staging or Development environment (Development temporarily for debugging)
        if (!_environment.IsEnvironment("Staging") && !_environment.IsDevelopment())
        {
            _logger.LogWarning("E2E Test: Access denied. Current environment: {Environment}, Expected: Staging or Development",
                _environment.EnvironmentName);
            return NotFound(new { error = "E2E endpoints only available in Staging or Development environment", currentEnvironment = _environment.EnvironmentName });
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
            // Add timeout to token validation to prevent hanging requests
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            // Validate Service Principal token using dedicated validator
            var validationTask = _tokenValidator.ValidateAsync(token);
            var isValid = await validationTask.WaitAsync(cts.Token);

            if (!isValid)
            {
                _logger.LogWarning("E2E Test: Service Principal token validation failed");
                return Unauthorized("Invalid Service Principal token");
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("E2E Test: Token validation timed out after 30 seconds");
            return StatusCode(408, new { error = "Token validation timeout", details = "Validation process exceeded maximum allowed time" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "E2E Test: Error validating Service Principal token");
            return StatusCode(500, new { error = "Token validation error", details = ex.Message });
        }

        // Create synthetic user session using dedicated service
        await _sessionService.CreateE2EAuthenticationSessionAsync(HttpContext, _environment.EnvironmentName);

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
    [Authorize(AuthenticationSchemes = "E2ETestCookies")]
    public ActionResult<object> GetAuthStatus()
    {
        // CRITICAL SECURITY: Only allow in Staging or Development environment (Development temporarily for debugging)
        if (!_environment.IsEnvironment("Staging") && !_environment.IsDevelopment())
        {
            return NotFound(new { error = "E2E endpoints only available in Staging or Development environment", currentEnvironment = _environment.EnvironmentName });
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
    [Authorize(AuthenticationSchemes = "E2ETestCookies")]
    public ActionResult GetE2EApp()
    {
        // CRITICAL SECURITY: Only allow in Staging or Development environment (Development temporarily for debugging)
        if (!_environment.IsEnvironment("Staging") && !_environment.IsDevelopment())
        {
            return NotFound(new { error = "E2E endpoints only available in Staging or Development environment", currentEnvironment = _environment.EnvironmentName });
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

}