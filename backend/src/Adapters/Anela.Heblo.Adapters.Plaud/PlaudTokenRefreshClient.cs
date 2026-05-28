using System.Net.Http.Json;

namespace Anela.Heblo.Adapters.Plaud;

public sealed class PlaudTokenRefreshClient(HttpClient http) : IPlaudTokenRefreshClient
{
    public async Task<PlaudTokens> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsync(
            "https://platform.plaud.ai/developer/api/oauth/third-party/access-token/refresh",
            new { refresh_token = refreshToken },
            ct);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<PlaudTokens>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Empty refresh response from Plaud API");
    }
}
