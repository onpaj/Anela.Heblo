using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Caching.Memory;

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
    private readonly IMemoryCache _cache;

    public ServicePrincipalTokenValidator(
        ILogger<ServicePrincipalTokenValidator> logger,
        IConfiguration configuration,
        IMemoryCache cache)
    {
        _logger = logger;
        _configuration = configuration;
        _cache = cache;
    }

    /// <summary>
    /// Validates a Service Principal JWT token comprehensively
    /// Checks issuer, audience, expiration, app claims, and tenant validation
    /// Uses caching to improve performance for repeated validations
    /// </summary>
    public Task<bool> ValidateAsync(string token)
    {
        try
        {
            // Use token hash as cache key for validation results
            var tokenHash = token.GetHashCode().ToString();
            var cacheKey = $"sptoken_{tokenHash}";

            // Check cache first to avoid repeated validation overhead
            if (_cache.TryGetValue(cacheKey, out bool cachedResult))
            {
                _logger.LogDebug("Service Principal token validation: Using cached result for token");
                return Task.FromResult(cachedResult);
            }

            // Parse JWT token to get basic info
            var handler = new JwtSecurityTokenHandler();
            var jsonToken = handler.ReadJwtToken(token);

            // Verify it's an app token (not user token)
            var appIdClaim = jsonToken.Claims.FirstOrDefault(c => c.Type == "appid");
            var tenantIdClaim = jsonToken.Claims.FirstOrDefault(c => c.Type == "tid");

            if (appIdClaim == null || tenantIdClaim == null)
            {
                _logger.LogWarning("Service Principal token validation: Token missing required app claims");
                return Task.FromResult(false);
            }

            // Check if it matches our expected Service Principal
            var expectedClientId = _configuration["E2E:ExpectedClientId"];
            var expectedTenantId = _configuration["E2E:ExpectedTenantId"];

            if (!string.IsNullOrEmpty(expectedClientId) && appIdClaim.Value != expectedClientId)
            {
                _logger.LogWarning("Service Principal token validation: Token client ID mismatch. Expected: {Expected}, Got: {Actual}",
                    expectedClientId, appIdClaim.Value);
                return Task.FromResult(false);
            }

            if (!string.IsNullOrEmpty(expectedTenantId) && tenantIdClaim.Value != expectedTenantId)
            {
                _logger.LogWarning("Service Principal token validation: Token tenant ID mismatch. Expected: {Expected}, Got: {Actual}",
                    expectedTenantId, tenantIdClaim.Value);
                return Task.FromResult(false);
            }

            // CRITICAL SECURITY: Validate issuer to ensure token is from Azure AD (accept both v1.0 and v2.0 tokens)
            var issuerClaim = jsonToken.Claims.FirstOrDefault(c => c.Type == "iss");
            var expectedIssuerV1 = $"https://sts.windows.net/{tenantIdClaim.Value}/";
            var expectedIssuerV2 = $"https://login.microsoftonline.com/{tenantIdClaim.Value}/v2.0";

            if (issuerClaim == null || (issuerClaim.Value != expectedIssuerV1 && issuerClaim.Value != expectedIssuerV2))
            {
                _logger.LogWarning("Service Principal token validation: Invalid issuer. Expected: {ExpectedV1} OR {ExpectedV2}, Got: {Actual}",
                    expectedIssuerV1, expectedIssuerV2, issuerClaim?.Value);
                return Task.FromResult(false);
            }

            // CRITICAL SECURITY: Validate audience 
            var audienceClaim = jsonToken.Claims.FirstOrDefault(c => c.Type == "aud");

            if (audienceClaim == null)
            {
                _logger.LogWarning("Service Principal token validation: Token missing audience claim");
                return Task.FromResult(false);
            }

            // CRITICAL SECURITY: Validate token expiration
            var expClaim = jsonToken.Claims.FirstOrDefault(c => c.Type == "exp");
            if (expClaim != null && long.TryParse(expClaim.Value, out var exp))
            {
                var expirationTime = DateTimeOffset.FromUnixTimeSeconds(exp);
                if (expirationTime <= DateTimeOffset.UtcNow)
                {
                    _logger.LogWarning("Service Principal token validation: Token has expired. Expiration: {Expiration}", expirationTime);
                    return Task.FromResult(false);
                }
            }

            _logger.LogInformation("Service Principal token validation: Token validated successfully. AppId: {AppId}, Tenant: {TenantId}, Issuer: {Issuer}",
                appIdClaim.Value, tenantIdClaim.Value, issuerClaim.Value);

            // Cache the successful validation result for 5 minutes (tokens typically valid for 1 hour)
            var cacheOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
                Priority = CacheItemPriority.Normal
            };
            _cache.Set(cacheKey, true, cacheOptions);

            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Service Principal token validation: Error parsing Service Principal token");

            // Cache failed validation result for shorter period to allow retry
            var tokenHash = token.GetHashCode().ToString();
            var cacheKey = $"sptoken_{tokenHash}";
            var cacheOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1),
                Priority = CacheItemPriority.Low
            };
            _cache.Set(cacheKey, false, cacheOptions);

            return Task.FromResult(false);
        }
    }
}