using System.Text.Json.Serialization;

namespace Anela.Heblo.Adapters.Plaud;

public sealed record PlaudTokens(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("expires_at")] long ExpiresAt);
