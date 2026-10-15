using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.API.Infrastructure.Authentication;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Anela.Heblo.Tests.Infrastructure.Authentication;

/// <summary>
/// The validator gates a super_user session, so every test signs tokens with a local RSA key
/// handed over through <see cref="IE2ETokenSigningKeySource"/>. Nothing here calls Entra.
/// </summary>
public class ServicePrincipalTokenValidatorTests
{
    private const string TenantId = "31fd4df1-b9c0-4abd-a4b0-0e1aceaabe9a";
    private const string E2EClientId = "f0b13796-b193-40ee-a698-ca76f347d06b";
    private const string ApiClientId = "8b34be89-f86f-422f-af40-7dbcd30cb66a";
    private const string IssuerV1 = $"https://sts.windows.net/{TenantId}/";
    private const string IssuerV2 = $"https://login.microsoftonline.com/{TenantId}/v2.0";
    private const string AudienceUri = $"api://{ApiClientId}";

    private readonly RsaSecurityKey _entraKey = CreateRsaKey("entra-key");
    private readonly RsaSecurityKey _foreignKey = CreateRsaKey("foreign-key");
    private readonly SymmetricSecurityKey _hmacKey = new(RandomNumberGenerator.GetBytes(64)) { KeyId = "hmac-key" };

    [Fact]
    public async Task ValidateAsync_ValidV1Token_ReturnsTrue()
    {
        var validator = CreateValidator();

        var result = await validator.ValidateAsync(CreateToken());

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_ValidV2TokenWithBareGuidAudience_ReturnsTrue()
    {
        var validator = CreateValidator();

        // Entra v2 access tokens name the calling app in azp, not appid.
        var result = await validator.ValidateAsync(
            CreateToken(issuer: IssuerV2, audience: ApiClientId, appId: null, azp: E2EClientId));

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_V2TokenWithWrongAzp_ReturnsFalse()
    {
        var validator = CreateValidator();

        var result = await validator.ValidateAsync(CreateToken(
            issuer: IssuerV2, audience: ApiClientId, appId: null, azp: "11111111-1111-1111-1111-111111111111"));

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_TokenWithUnknownKeyId_RefreshesKeysAndRetriesOnce()
    {
        // Entra rotated its signing key after our cached JWKS was fetched.
        var rotatedKey = CreateRsaKey("rotated-key");
        var keySource = new RotatingKeySource(before: _entraKey, after: rotatedKey);
        var validator = CreateValidator(keySource: keySource);

        var result = await validator.ValidateAsync(CreateToken(signingKey: rotatedKey));

        result.Should().BeTrue();
        keySource.RefreshRequests.Should().Be(1);
    }

    [Fact]
    public async Task ValidateAsync_TokenSignedByForeignKey_RetriesOnlyOnce()
    {
        var keySource = new FakeKeySource(_entraKey);
        var validator = CreateValidator(keySource: keySource);

        var result = await validator.ValidateAsync(CreateToken(signingKey: _foreignKey));

        result.Should().BeFalse();
        keySource.RefreshRequests.Should().Be(1);
        keySource.Calls.Should().Be(2);
    }

    [Fact]
    public async Task ValidateAsync_TokenSignedByAnotherKey_ReturnsFalse()
    {
        var validator = CreateValidator();

        var result = await validator.ValidateAsync(CreateToken(signingKey: _foreignKey));

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_UnsignedAlgNoneToken_ReturnsFalse()
    {
        var validator = CreateValidator();
        var signed = CreateToken();
        var payload = signed.Split('.')[1];
        var unsigned = $"{Base64UrlEncoder.Encode("{\"alg\":\"none\",\"typ\":\"JWT\"}")}.{payload}.";

        var result = await validator.ValidateAsync(unsigned);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_HmacTokenSignedWithKnownKey_ReturnsFalse()
    {
        // Even if a symmetric key ever ended up in the key set, only RS256 is accepted.
        var validator = CreateValidator(keys: new SecurityKey[] { _entraKey, _hmacKey });
        var token = CreateToken(signingCredentials: new SigningCredentials(_hmacKey, SecurityAlgorithms.HmacSha256));

        var result = await validator.ValidateAsync(token);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_TamperedPayload_ReturnsFalse()
    {
        var validator = CreateValidator();
        var parts = CreateToken(appId: "11111111-1111-1111-1111-111111111111").Split('.');
        var forgedPayload = Base64UrlEncoder.Encode(
            Base64UrlEncoder.Decode(parts[1]).Replace("11111111-1111-1111-1111-111111111111", E2EClientId));
        var tampered = $"{parts[0]}.{forgedPayload}.{parts[2]}";

        var result = await validator.ValidateAsync(tampered);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_WrongAudience_ReturnsFalse()
    {
        var validator = CreateValidator();

        var result = await validator.ValidateAsync(CreateToken(audience: "api://some-other-api"));

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_IssuerOfAnotherTenant_ReturnsFalse()
    {
        // A token issued by a different tenant, carrying that tenant's own tid, must not pass:
        // the issuer is pinned to the configured tenant, not to the token's tid.
        var validator = CreateValidator();
        const string otherTenant = "99999999-9999-9999-9999-999999999999";

        var result = await validator.ValidateAsync(
            CreateToken(issuer: $"https://sts.windows.net/{otherTenant}/", tenantId: otherTenant));

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_WrongAppId_ReturnsFalse()
    {
        var validator = CreateValidator();

        var result = await validator.ValidateAsync(CreateToken(appId: "11111111-1111-1111-1111-111111111111"));

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_DelegatedUserTokenFromE2EApp_ReturnsFalse()
    {
        // A user token obtained through the E2E app has the same appid but carries scp; only app-only tokens count.
        var validator = CreateValidator();

        var result = await validator.ValidateAsync(CreateToken(scope: "access_as_user"));

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_MissingAppId_ReturnsFalse()
    {
        var validator = CreateValidator();

        var result = await validator.ValidateAsync(CreateToken(appId: null));

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_ExpiredToken_ReturnsFalse()
    {
        var validator = CreateValidator();
        var now = DateTime.UtcNow;

        var result = await validator.ValidateAsync(
            CreateToken(notBefore: now.AddHours(-2), expires: now.AddMinutes(-30)));

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_GarbageToken_ReturnsFalse()
    {
        var validator = CreateValidator();

        var result = await validator.ValidateAsync("invalid-token");

        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("E2E:ExpectedClientId")]
    [InlineData("E2E:ExpectedTenantId")]
    [InlineData("AzureAd:ClientId")]
    public async Task ValidateAsync_RequiredConfigurationEmpty_FailsClosed(string emptyKey)
    {
        var keySource = new FakeKeySource(_entraKey);
        var validator = CreateValidator(keySource: keySource, overrides: new() { [emptyKey] = "" });

        var result = await validator.ValidateAsync(CreateToken());

        result.Should().BeFalse();
        keySource.Calls.Should().Be(0, "nothing should be fetched when the expected identity is unknown");
    }

    [Fact]
    public async Task ValidateAsync_KeySourceThrows_ReturnsFalse()
    {
        var validator = CreateValidator(keySource: new ThrowingKeySource());

        var result = await validator.ValidateAsync(CreateToken());

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_SuccessForOneToken_DoesNotAcceptAnotherToken()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var validator = CreateValidator(cache: cache);
        (await validator.ValidateAsync(CreateToken())).Should().BeTrue();

        var result = await validator.ValidateAsync(CreateToken(signingKey: _foreignKey));

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_RepeatedValidToken_IsServedFromCache()
    {
        var keySource = new FakeKeySource(_entraKey);
        var validator = CreateValidator(keySource: keySource, cache: new MemoryCache(new MemoryCacheOptions()));
        var token = CreateToken();

        (await validator.ValidateAsync(token)).Should().BeTrue();
        (await validator.ValidateAsync(token)).Should().BeTrue();

        keySource.Calls.Should().Be(1);
    }

    [Fact]
    public async Task ValidateAsync_CachedSuccess_NeverOutlivesTokenExpiry()
    {
        var cache = new RecordingMemoryCache();
        var validator = CreateValidator(cache: cache);
        var expires = DateTime.UtcNow.AddMinutes(1);

        (await validator.ValidateAsync(CreateToken(expires: expires))).Should().BeTrue();

        cache.Entries.Should().ContainSingle();
        var entry = cache.Entries[0];
        var effectiveExpiry = entry.AbsoluteExpiration
            ?? DateTimeOffset.UtcNow + entry.AbsoluteExpirationRelativeToNow!.Value;
        effectiveExpiry.Should().BeOnOrBefore(new DateTimeOffset(expires).AddSeconds(1));
    }

    [Fact]
    public async Task ValidateAsync_InvalidToken_IsNotCached()
    {
        var cache = new RecordingMemoryCache();
        var validator = CreateValidator(cache: cache);

        (await validator.ValidateAsync(CreateToken(signingKey: _foreignKey))).Should().BeFalse();

        cache.Entries.Should().BeEmpty();
    }

    private ServicePrincipalTokenValidator CreateValidator(
        IE2ETokenSigningKeySource? keySource = null,
        IMemoryCache? cache = null,
        IReadOnlyCollection<SecurityKey>? keys = null,
        Dictionary<string, string?>? overrides = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["E2E:ExpectedClientId"] = E2EClientId,
            ["E2E:ExpectedTenantId"] = TenantId,
            ["AzureAd:ClientId"] = ApiClientId,
        };
        foreach (var (key, value) in overrides ?? new())
        {
            settings[key] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        return new ServicePrincipalTokenValidator(
            NullLogger<ServicePrincipalTokenValidator>.Instance,
            configuration,
            cache ?? new MemoryCache(new MemoryCacheOptions()),
            keySource ?? new FakeKeySource(keys ?? new SecurityKey[] { _entraKey }));
    }

    private string CreateToken(
        string issuer = IssuerV1,
        string audience = AudienceUri,
        string? appId = E2EClientId,
        string? azp = null,
        string? scope = null,
        string tenantId = TenantId,
        RsaSecurityKey? signingKey = null,
        SigningCredentials? signingCredentials = null,
        DateTime? notBefore = null,
        DateTime? expires = null)
    {
        var now = DateTime.UtcNow;
        var claims = new Dictionary<string, object> { ["tid"] = tenantId };
        if (appId != null)
        {
            claims["appid"] = appId;
        }

        if (azp != null)
        {
            claims["azp"] = azp;
        }

        if (scope != null)
        {
            claims["scp"] = scope;
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Claims = claims,
            IssuedAt = notBefore ?? now.AddMinutes(-1),
            NotBefore = notBefore ?? now.AddMinutes(-1),
            Expires = expires ?? now.AddHours(1),
            SigningCredentials = signingCredentials
                ?? new SigningCredentials(signingKey ?? _entraKey, SecurityAlgorithms.RsaSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private static RsaSecurityKey CreateRsaKey(string keyId) => new(RSA.Create(2048)) { KeyId = keyId };

    private sealed class FakeKeySource : IE2ETokenSigningKeySource
    {
        private readonly IReadOnlyCollection<SecurityKey> _keys;

        public FakeKeySource(params SecurityKey[] keys) : this((IReadOnlyCollection<SecurityKey>)keys) { }

        public FakeKeySource(IReadOnlyCollection<SecurityKey> keys) => _keys = keys;

        public int Calls { get; private set; }

        public int RefreshRequests { get; private set; }

        public Task<IReadOnlyCollection<SecurityKey>> GetSigningKeysAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(_keys);
        }

        public void RequestRefresh() => RefreshRequests++;
    }

    private sealed class RotatingKeySource : IE2ETokenSigningKeySource
    {
        private readonly SecurityKey _before;
        private readonly SecurityKey _after;

        public RotatingKeySource(SecurityKey before, SecurityKey after)
        {
            _before = before;
            _after = after;
        }

        public int RefreshRequests { get; private set; }

        public Task<IReadOnlyCollection<SecurityKey>> GetSigningKeysAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<SecurityKey>>(
                RefreshRequests == 0 ? new[] { _before } : new[] { _before, _after });

        public void RequestRefresh() => RefreshRequests++;
    }

    private sealed class ThrowingKeySource : IE2ETokenSigningKeySource
    {
        public Task<IReadOnlyCollection<SecurityKey>> GetSigningKeysAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("metadata endpoint unreachable");

        public void RequestRefresh() { }
    }

    /// <summary>Captures entry options so a test can see how long a result would stay cached.</summary>
    private sealed class RecordingMemoryCache : IMemoryCache
    {
        private readonly MemoryCache _inner = new(new MemoryCacheOptions());

        public List<ICacheEntry> Entries { get; } = new();

        public ICacheEntry CreateEntry(object key)
        {
            var entry = _inner.CreateEntry(key);
            Entries.Add(entry);
            return entry;
        }

        public bool TryGetValue(object key, out object? value) => _inner.TryGetValue(key, out value);

        public void Remove(object key) => _inner.Remove(key);

        public void Dispose() => _inner.Dispose();
    }
}
