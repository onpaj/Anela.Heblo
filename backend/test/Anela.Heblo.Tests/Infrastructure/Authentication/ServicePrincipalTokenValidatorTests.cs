using System;
using System.IdentityModel.Tokens.Jwt;
using System.Threading.Tasks;
using Anela.Heblo.API.Infrastructure.Authentication;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Anela.Heblo.Tests.Infrastructure.Authentication;

/// <summary>
/// Tests for Service Principal Token Validator performance optimizations
/// Validates caching behavior and timeout handling improvements
/// </summary>
public class ServicePrincipalTokenValidatorTests
{
    private readonly ILogger<ServicePrincipalTokenValidator> _logger;
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _cache;

    public ServicePrincipalTokenValidatorTests()
    {
        _logger = NullLogger<ServicePrincipalTokenValidator>.Instance;
        _cache = new MemoryCache(new MemoryCacheOptions());

        // Create minimal configuration for testing
        var configBuilder = new ConfigurationBuilder();
        configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            {"E2E:ExpectedClientId", "test-client-id"},
            {"E2E:ExpectedTenantId", "test-tenant-id"}
        });
        _configuration = configBuilder.Build();
    }

    [Fact]
    public async Task ValidateAsync_WithValidToken_ShouldCacheResult()
    {
        // Arrange
        var validator = new ServicePrincipalTokenValidator(_logger, _configuration, _cache);
        var validToken = CreateValidTestToken();

        // Act - First validation
        var result1 = await validator.ValidateAsync(validToken);

        // Act - Second validation (should use cache)
        var result2 = await validator.ValidateAsync(validToken);

        // Assert
        Assert.True(result1);
        Assert.True(result2);

        // Verify cache contains the result
        var tokenHash = validToken.GetHashCode().ToString();
        var cacheKey = $"sptoken_{tokenHash}";
        Assert.True(_cache.TryGetValue(cacheKey, out bool cachedValue));
        Assert.True(cachedValue);
    }

    [Fact]
    public async Task ValidateAsync_WithInvalidToken_ShouldCacheFailureResult()
    {
        // Arrange
        var validator = new ServicePrincipalTokenValidator(_logger, _configuration, _cache);
        var invalidToken = "invalid-token";

        // Act
        var result = await validator.ValidateAsync(invalidToken);

        // Assert
        Assert.False(result);

        // Verify failure is cached (for shorter duration)
        var tokenHash = invalidToken.GetHashCode().ToString();
        var cacheKey = $"sptoken_{tokenHash}";
        Assert.True(_cache.TryGetValue(cacheKey, out bool cachedValue));
        Assert.False(cachedValue);
    }

    [Fact]
    public async Task ValidateAsync_WithCachedResult_ShouldReturnCachedValue()
    {
        // Arrange
        var validator = new ServicePrincipalTokenValidator(_logger, _configuration, _cache);
        var token = "test-token";
        var tokenHash = token.GetHashCode().ToString();
        var cacheKey = $"sptoken_{tokenHash}";

        // Pre-populate cache
        _cache.Set(cacheKey, true, TimeSpan.FromMinutes(5));

        // Act
        var result = await validator.ValidateAsync(token);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task ValidateAsync_TokenMissingRequiredClaims_ShouldReturnFalse()
    {
        // Arrange
        var validator = new ServicePrincipalTokenValidator(_logger, _configuration, _cache);
        var tokenWithoutClaims = CreateTokenWithoutRequiredClaims();

        // Act
        var result = await validator.ValidateAsync(tokenWithoutClaims);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ValidateAsync_ExpiredToken_ShouldReturnFalse()
    {
        // Arrange
        var validator = new ServicePrincipalTokenValidator(_logger, _configuration, _cache);
        var expiredToken = CreateExpiredTestToken();

        // Act
        var result = await validator.ValidateAsync(expiredToken);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ValidateAsync_TokenWithWrongClientId_ShouldReturnFalse()
    {
        // Arrange
        var validator = new ServicePrincipalTokenValidator(_logger, _configuration, _cache);
        var tokenWithWrongClientId = CreateTokenWithWrongClientId();

        // Act
        var result = await validator.ValidateAsync(tokenWithWrongClientId);

        // Assert
        Assert.False(result);
    }

    private string CreateValidTestToken()
    {
        var handler = new JwtSecurityTokenHandler();
        var tokenDescriptor = new Microsoft.IdentityModel.Tokens.SecurityTokenDescriptor
        {
            Claims = new System.Collections.Generic.Dictionary<string, object>
            {
                ["appid"] = "test-client-id",
                ["tid"] = "test-tenant-id",
                ["iss"] = "https://login.microsoftonline.com/test-tenant-id/v2.0",
                ["aud"] = "api://test-audience",
                ["exp"] = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
            },
            Expires = DateTime.UtcNow.AddHours(1)
        };

        var token = handler.CreateJwtSecurityToken(tokenDescriptor);
        return handler.WriteToken(token);
    }

    private string CreateTokenWithoutRequiredClaims()
    {
        var handler = new JwtSecurityTokenHandler();
        var tokenDescriptor = new Microsoft.IdentityModel.Tokens.SecurityTokenDescriptor
        {
            Claims = new System.Collections.Generic.Dictionary<string, object>
            {
                ["iss"] = "https://login.microsoftonline.com/test-tenant-id/v2.0",
                ["aud"] = "api://test-audience"
                // Missing appid and tid claims
            },
            Expires = DateTime.UtcNow.AddHours(1)
        };

        var token = handler.CreateJwtSecurityToken(tokenDescriptor);
        return handler.WriteToken(token);
    }

    private string CreateExpiredTestToken()
    {
        var handler = new JwtSecurityTokenHandler();
        var expiredTime = DateTime.UtcNow.AddHours(-1);
        var notBeforeTime = DateTime.UtcNow.AddHours(-2); // NotBefore must be before Expires
        var tokenDescriptor = new Microsoft.IdentityModel.Tokens.SecurityTokenDescriptor
        {
            Claims = new System.Collections.Generic.Dictionary<string, object>
            {
                ["appid"] = "test-client-id",
                ["tid"] = "test-tenant-id",
                ["iss"] = "https://login.microsoftonline.com/test-tenant-id/v2.0",
                ["aud"] = "api://test-audience",
                ["exp"] = ((DateTimeOffset)expiredTime).ToUnixTimeSeconds() // Expired 1 hour ago
            },
            Expires = expiredTime,
            NotBefore = notBeforeTime
        };

        var token = handler.CreateJwtSecurityToken(tokenDescriptor);
        return handler.WriteToken(token);
    }

    private string CreateTokenWithWrongClientId()
    {
        var handler = new JwtSecurityTokenHandler();
        var tokenDescriptor = new Microsoft.IdentityModel.Tokens.SecurityTokenDescriptor
        {
            Claims = new System.Collections.Generic.Dictionary<string, object>
            {
                ["appid"] = "wrong-client-id", // Different from expected
                ["tid"] = "test-tenant-id",
                ["iss"] = "https://login.microsoftonline.com/test-tenant-id/v2.0",
                ["aud"] = "api://test-audience",
                ["exp"] = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
            },
            Expires = DateTime.UtcNow.AddHours(1)
        };

        var token = handler.CreateJwtSecurityToken(tokenDescriptor);
        return handler.WriteToken(token);
    }
}