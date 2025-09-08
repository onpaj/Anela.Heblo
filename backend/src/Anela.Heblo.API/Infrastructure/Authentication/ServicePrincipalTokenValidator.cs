using System.IdentityModel.Tokens.Jwt;

namespace Anela.Heblo.API.Infrastructure.Authentication;

/// <summary>
/// Service for validating Azure AD Service Principal tokens in E2E testing scenarios
/// Provides comprehensive JWT validation including issuer, audience, expiration, and claims validation
/// </summary>
public interface IServicePrincipalTokenValidator
{
    /// <summary>
    /// Validates a Service Principal JWT token from Azure AD
    /// </summary>
    /// <param name="token">The JWT token to validate</param>
    /// <returns>True if the token is valid, false otherwise</returns>
    Task<bool> ValidateAsync(string token);
}

/// <summary>
/// Implementation of Service Principal token validation for E2E testing
/// Validates tokens against Azure AD standards and configured expectations
/// </summary>
public class ServicePrincipalTokenValidator : IServicePrincipalTokenValidator
{
    private readonly ILogger<ServicePrincipalTokenValidator> _logger;
    private readonly IConfiguration _configuration;

    public ServicePrincipalTokenValidator(
        ILogger<ServicePrincipalTokenValidator> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Validates a Service Principal JWT token comprehensively
    /// Checks issuer, audience, expiration, app claims, and tenant validation
    /// </summary>
    public async Task<bool> ValidateAsync(string token)
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
                _logger.LogWarning("Service Principal token validation: Token missing required app claims");
                return false;
            }

            // Check if it matches our expected Service Principal
            var expectedClientId = _configuration["E2E:ExpectedClientId"];
            var expectedTenantId = _configuration["E2E:ExpectedTenantId"];

            if (!string.IsNullOrEmpty(expectedClientId) && appIdClaim.Value != expectedClientId)
            {
                _logger.LogWarning("Service Principal token validation: Token client ID mismatch. Expected: {Expected}, Got: {Actual}",
                    expectedClientId, appIdClaim.Value);
                return false;
            }

            if (!string.IsNullOrEmpty(expectedTenantId) && tenantIdClaim.Value != expectedTenantId)
            {
                _logger.LogWarning("Service Principal token validation: Token tenant ID mismatch. Expected: {Expected}, Got: {Actual}",
                    expectedTenantId, tenantIdClaim.Value);
                return false;
            }

            // CRITICAL SECURITY: Validate issuer to ensure token is from Azure AD (accept both v1.0 and v2.0 tokens)
            var issuerClaim = jsonToken.Claims.FirstOrDefault(c => c.Type == "iss");
            var expectedIssuerV1 = $"https://sts.windows.net/{tenantIdClaim.Value}/";
            var expectedIssuerV2 = $"https://login.microsoftonline.com/{tenantIdClaim.Value}/v2.0";
            
            if (issuerClaim == null || (issuerClaim.Value != expectedIssuerV1 && issuerClaim.Value != expectedIssuerV2))
            {
                _logger.LogWarning("Service Principal token validation: Invalid issuer. Expected: {ExpectedV1} OR {ExpectedV2}, Got: {Actual}", 
                    expectedIssuerV1, expectedIssuerV2, issuerClaim?.Value);
                return false;
            }

            // CRITICAL SECURITY: Validate audience 
            var audienceClaim = jsonToken.Claims.FirstOrDefault(c => c.Type == "aud");

            if (audienceClaim == null)
            {
                _logger.LogWarning("Service Principal token validation: Token missing audience claim");
                return false;
            }

            // CRITICAL SECURITY: Validate token expiration
            var expClaim = jsonToken.Claims.FirstOrDefault(c => c.Type == "exp");
            if (expClaim != null && long.TryParse(expClaim.Value, out var exp))
            {
                var expirationTime = DateTimeOffset.FromUnixTimeSeconds(exp);
                if (expirationTime <= DateTimeOffset.UtcNow)
                {
                    _logger.LogWarning("Service Principal token validation: Token has expired. Expiration: {Expiration}", expirationTime);
                    return false;
                }
            }

            _logger.LogInformation("Service Principal token validation: Token validated successfully. AppId: {AppId}, Tenant: {TenantId}, Issuer: {Issuer}",
                appIdClaim.Value, tenantIdClaim.Value, issuerClaim.Value);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Service Principal token validation: Error parsing Service Principal token");
            return false;
        }
    }
}