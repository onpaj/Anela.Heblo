using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

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
/// Validates the Service Principal token that buys an E2E session (a super_user).
/// The token must be RS256-signed by the configured tenant's Entra keys, issued by that tenant,
/// addressed to this API (AzureAd:ClientId) and requested by the configured E2E app (E2E:ExpectedClientId).
/// Missing configuration fails closed.
/// </summary>
public class ServicePrincipalTokenValidator : IServicePrincipalTokenValidator
{
    public const string ExpectedClientIdKey = "E2E:ExpectedClientId";
    public const string ExpectedTenantIdKey = "E2E:ExpectedTenantId";
    public const string ApiClientIdKey = "AzureAd:ClientId";

    private const string AppIdClaim = "appid";
    private const string CacheKeyPrefix = "sptoken_";
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan MaxCacheDuration = TimeSpan.FromMinutes(5);

    private readonly ILogger<ServicePrincipalTokenValidator> _logger;
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _cache;
    private readonly IE2ETokenSigningKeySource _signingKeySource;

    public ServicePrincipalTokenValidator(
        ILogger<ServicePrincipalTokenValidator> logger,
        IConfiguration configuration,
        IMemoryCache cache,
        IE2ETokenSigningKeySource signingKeySource)
    {
        _logger = logger;
        _configuration = configuration;
        _cache = cache;
        _signingKeySource = signingKeySource;
    }

    public async Task<bool> ValidateAsync(string token)
    {
        var expectedClientId = _configuration[ExpectedClientIdKey];
        var expectedTenantId = _configuration[ExpectedTenantIdKey];
        var apiClientId = _configuration[ApiClientIdKey];

        if (string.IsNullOrWhiteSpace(expectedClientId)
            || string.IsNullOrWhiteSpace(expectedTenantId)
            || string.IsNullOrWhiteSpace(apiClientId))
        {
            _logger.LogError(
                "Service Principal token validation: {ClientIdKey}, {TenantIdKey} and {ApiClientIdKey} must all be configured; rejecting token",
                ExpectedClientIdKey, ExpectedTenantIdKey, ApiClientIdKey);
            return false;
        }

        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        var cacheKey = CacheKeyPrefix + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        if (_cache.TryGetValue(cacheKey, out bool _))
        {
            _logger.LogDebug("Service Principal token validation: Using cached result for token");
            return true;
        }

        try
        {
            var signingKeys = await _signingKeySource.GetSigningKeysAsync(CancellationToken.None);
            var parameters = CreateValidationParameters(expectedTenantId, apiClientId, signingKeys);
            var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, parameters);

            if (!result.IsValid)
            {
                _logger.LogWarning("Service Principal token validation: Token rejected ({Reason})",
                    result.Exception?.GetType().Name ?? "unknown");
                return false;
            }

            var appId = result.Claims.TryGetValue(AppIdClaim, out var appIdValue) ? appIdValue as string : null;
            if (!string.Equals(appId, expectedClientId, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Service Principal token validation: Token client ID mismatch. Expected: {Expected}, Got: {Actual}",
                    expectedClientId, appId);
                return false;
            }

            CacheSuccess(cacheKey, result.SecurityToken.ValidTo);

            _logger.LogInformation("Service Principal token validation: Token validated successfully. AppId: {AppId}, Issuer: {Issuer}",
                appId, result.Issuer);
            return true;
        }
        catch (Exception ex)
        {
            // Covers an unreachable Entra metadata endpoint; the token itself is never logged.
            _logger.LogError(ex, "Service Principal token validation: Error validating Service Principal token");
            return false;
        }
    }

    private static TokenValidationParameters CreateValidationParameters(
        string tenantId, string apiClientId, IReadOnlyCollection<SecurityKey> signingKeys) => new()
        {
            ValidIssuers = new[]
            {
                $"https://sts.windows.net/{tenantId}/",
                $"https://login.microsoftonline.com/{tenantId}/v2.0",
            },
            ValidAudiences = new[] { $"api://{apiClientId}", apiClientId },
            IssuerSigningKeys = signingKeys,
            ValidAlgorithms = new[] { SecurityAlgorithms.RsaSha256 },
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ClockSkew = ClockSkew,
        };

    private void CacheSuccess(string cacheKey, DateTime validToUtc)
    {
        // Never keep a success past the token's own expiry.
        var expiresAt = new DateTimeOffset(DateTime.SpecifyKind(validToUtc, DateTimeKind.Utc));
        var cap = DateTimeOffset.UtcNow + MaxCacheDuration;
        var absoluteExpiration = expiresAt < cap ? expiresAt : cap;
        if (absoluteExpiration <= DateTimeOffset.UtcNow)
        {
            return;
        }

        _cache.Set(cacheKey, true, new MemoryCacheEntryOptions
        {
            AbsoluteExpiration = absoluteExpiration,
            Priority = CacheItemPriority.Normal,
        });
    }
}
