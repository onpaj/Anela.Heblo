using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.Plaud;

public sealed class PlaudTokenRefreshClient(HttpClient http) : IPlaudTokenRefreshClient
{
    private const string RefreshUrl =
        "https://platform.plaud.ai/developer/api/oauth/third-party/access-token/refresh";

    private const long MillisecondsPerSecond = 1000L;

    public async Task<PlaudTokens> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        // The Plaud OAuth refresh endpoint expects an application/x-www-form-urlencoded body,
        // not JSON. Sending JSON returns 422 Unprocessable Entity. This mirrors @plaud-ai/cli.
        using var request = new HttpRequestMessage(HttpMethod.Post, RefreshUrl)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["refresh_token"] = refreshToken
            })
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await http.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"Plaud token refresh failed: {(int)response.StatusCode} {response.StatusCode}. Body: {body}");
        }

        var refreshed = await response.Content.ReadFromJsonAsync<PlaudRefreshResponse>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Empty refresh response from Plaud API");

        // The response returns expires_in (relative seconds), not expires_at. Compute the absolute
        // expiry as a Unix millisecond timestamp to match the format the CLI writes to disk.
        if (!refreshed.ExpiresIn.HasValue)
            throw new InvalidOperationException("Plaud API response missing expires_in.");

        var expiresAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + refreshed.ExpiresIn.Value * MillisecondsPerSecond;

        return new PlaudTokens(
            AccessToken: refreshed.AccessToken ?? string.Empty,
            // The endpoint may omit refresh_token when only the access token rotates — fall back.
            RefreshToken: refreshed.RefreshToken ?? refreshToken,
            ExpiresAt: expiresAt,
            TokenType: refreshed.TokenType ?? "Bearer");
    }

    private sealed record PlaudRefreshResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("token_type")] string? TokenType,
        [property: JsonPropertyName("expires_in")] long? ExpiresIn);
}
