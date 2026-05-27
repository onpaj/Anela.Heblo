# Plaud Token Auto-Refresh (Deferred)

> **Status:** Deferred — depends on Azure Key Vault infra for Heblo.
> Pick up when `rgHeblo` has a Key Vault provisioned and the App Service Managed Identity is configured.

## Root Cause of the Bootstrapper-Overwrite Problem

`PlaudTokenBootstrapper` (`backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudTokenBootstrapper.cs`)
writes `~/.plaud/tokens.json` from the App Service setting `Plaud__TokensJson` on every container start.

The Plaud CLI auto-refreshes its tokens on every call, so continuous 5-minute polling normally keeps the
refresh token alive indefinitely. However, a container restart re-seeds a potentially stale token from
the App Service setting. If the stored `refresh_token` has aged past Plaud's hard TTL, every subsequent
CLI call fails with `[AUTH_FAILED] Token invalid or expired`.

**Short-term mitigation (implemented):** `PlaudPollingJob` now has
`[AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]`, which prevents the
10× retry flood and throws `PlaudAuthExpiredException` with actionable message. An Azure Monitor alert
fires within 5 minutes of the first failure (see monitoring alert `Heblo-Plaud-AuthExpired`).

## Observed Refresh Endpoint

From `@plaud-ai/cli` source inspection:

```
POST https://platform.plaud.ai/developer/api/oauth/third-party/access-token/refresh
Content-Type: application/json

{
  "refresh_token": "<current_refresh_token>"
}
```

Response shape (observed):

```json
{
  "access_token": "...",
  "refresh_token": "...",
  "expires_at": 1234567890
}
```

> **Open question:** Confirm Plaud's refresh-token hard TTL by inspecting `expires_at` and observing
> rotation over several days after implementing this. The hard TTL appears to be ~30 days but is not
> officially documented.

## Proposed Design

### `PlaudTokenRefreshClient`

New HttpClient wrapper in `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/`:

```csharp
public sealed class PlaudTokenRefreshClient
{
    private readonly HttpClient _http;

    public PlaudTokenRefreshClient(HttpClient http) => _http = http;

    public async Task<PlaudTokens> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync(
            "https://platform.plaud.ai/developer/api/oauth/third-party/access-token/refresh",
            new { refresh_token = refreshToken },
            ct);

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PlaudTokens>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Empty refresh response from Plaud API");
    }
}

public sealed record PlaudTokens(string AccessToken, string RefreshToken, long ExpiresAt);
```

### `PlaudTokenRefreshJob`

New recurring job in `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Infrastructure/Jobs/`:

```csharp
[AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
public async Task ExecuteAsync(CancellationToken ct = default)
{
    // 1. Read current tokens JSON from Key Vault secret "plaud-tokens-json"
    // 2. Deserialize to extract refresh_token
    // 3. Call PlaudTokenRefreshClient.RefreshAsync
    // 4. Serialize new tokens to JSON
    // 5. Write back to Key Vault secret "plaud-tokens-json"
    // 6. Overwrite ~/.plaud/tokens.json (same as PlaudTokenBootstrapper does today)
}

public RecurringJobMetadata Metadata { get; } = new()
{
    JobName = "plaud-token-refresh",
    DisplayName = "Plaud — refresh auth token",
    CronExpression = "0 4 * * 0",  // weekly, Sunday 04:00
    DefaultIsEnabled = false
};
```

### Storage: Key Vault Secret

- Secret name: `plaud-tokens-json`
- Value: full content of `~/.plaud/tokens.json` (the JSON blob the CLI expects)
- **Change `PlaudTokenBootstrapper`** to read from KV on startup instead of from the App Service setting
  `Plaud__TokensJson`. This removes the restart-stale-token problem entirely.
- Remove `Plaud__TokensJson` App Service setting once KV is in place.

## Infra Prerequisites

1. Key Vault provisioned in `rgHeblo` (e.g. `kv-heblo`).
2. App Service Managed Identity (`Heblo`) granted `Key Vault Secrets Officer` on the single secret
   `plaud-tokens-json` (least privilege — not on the entire vault).
3. Add `Azure.Security.KeyVault.Secrets` NuGet to the infrastructure layer.

## Verification Queries (for after implementation)

```bash
# Confirm token refresh job ran successfully
az monitor app-insights query --app aiHeblo -g rgHeblo \
  --analytics-query "traces | where message contains 'plaud-token-refresh' | order by timestamp desc | take 10"

# Confirm no auth failures in the 7 days after implementation
az monitor app-insights query --app aiHeblo -g rgHeblo \
  --analytics-query "exceptions | where type endswith 'PlaudAuthExpiredException' | where timestamp > ago(7d) | count"
```
