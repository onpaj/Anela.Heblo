using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Adapters.GoogleAds.Api;

/// <summary>
/// Exchanges the configured refresh token for an access token and caches it until shortly before it
/// expires. A settings change (rotated refresh token in Key Vault) invalidates the cache.
/// </summary>
internal sealed class GoogleAdsOAuthTokenProvider : IGoogleAdsAccessTokenProvider, IDisposable
{
    internal const string HttpClientName = "GoogleAdsOAuth";
    private const int DefaultLifetimeSeconds = 3600;
    private static readonly Uri TokenEndpoint = new("https://oauth2.googleapis.com/token");
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(2);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<GoogleAdsSettings> _settings;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CachedToken? _cached;

    public GoogleAdsOAuthTokenProvider(
        IHttpClientFactory httpClientFactory, IOptionsMonitor<GoogleAdsSettings> settings, TimeProvider timeProvider)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings;
        _timeProvider = timeProvider;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        var settings = _settings.CurrentValue;
        var fingerprint = Fingerprint(settings);
        if (IsUsable(_cached, fingerprint))
            return _cached!.Value;

        await _gate.WaitAsync(ct);
        try
        {
            if (!IsUsable(_cached, fingerprint))
                _cached = await RefreshAsync(settings, fingerprint, ct);
            return _cached!.Value;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private bool IsUsable(CachedToken? token, string fingerprint) =>
        token is not null
        && token.Fingerprint == fingerprint
        && _timeProvider.GetUtcNow() < token.ExpiresAt - RefreshMargin;

    private async Task<CachedToken> RefreshAsync(GoogleAdsSettings settings, string fingerprint, CancellationToken ct)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = settings.OAuth2ClientId,
            ["client_secret"] = settings.OAuth2ClientSecret,
            ["refresh_token"] = settings.OAuth2RefreshToken,
        });
        using var response = await _httpClientFactory.CreateClient(HttpClientName).PostAsync(TokenEndpoint, content, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw Failure(response.StatusCode, body);

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var token = root.TryGetProperty("access_token", out var t) ? t.GetString() : null;
        if (string.IsNullOrEmpty(token))
            throw new GoogleAdsApiException(
                "Google OAuth token response had no access_token.", response.StatusCode, "oauth.no_access_token", null, isTransient: false);

        var lifetime = root.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var seconds)
            ? seconds
            : DefaultLifetimeSeconds;
        return new CachedToken(token, _timeProvider.GetUtcNow().AddSeconds(lifetime), fingerprint);
    }

    private static GoogleAdsApiException Failure(HttpStatusCode status, string body)
    {
        var code = ReadOAuthErrorCode(body) ?? "unknown";
        return new GoogleAdsApiException(
            $"Google OAuth token refresh failed: HTTP {(int)status}, error '{code}'. 'invalid_grant' means the refresh " +
            "token was revoked or expired (a consent screen left in Testing status expires it after 7 days).",
            status, $"oauth.{code}", null, isTransient: (int)status >= 500);
    }

    private static string? ReadOAuthErrorCode(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty("error", out var error)
                   && error.ValueKind == JsonValueKind.String
                ? error.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Hashed so the cache key never holds the secret in clear text.
    private static string Fingerprint(GoogleAdsSettings s) =>
        Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes($"{s.OAuth2ClientId}|{s.OAuth2ClientSecret}|{s.OAuth2RefreshToken}")));

    private sealed record CachedToken(string Value, DateTimeOffset ExpiresAt, string Fingerprint);
}
