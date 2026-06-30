# Plaud Token Auto-Refresh (Reactive — updated 2026-06-19)

> **Status:** Implemented — token refresh is **reactive**. When any Plaud CLI call fails with
> `AUTH_FAILED`, `PlaudCliClient` refreshes the token in-process, persists it to disk and (when
> configured) Key Vault `Plaud--TokensJson`, then retries the call once. The previous standalone
> weekly Hangfire job (`plaud-token-refresh`) has been **removed** — reactive refresh plus the
> 5-minute `plaud-polling` job keeps the token alive and recovers within a single failed call.

## How It Works

1. A Plaud CLI invocation fails with `[AUTH_FAILED] Token invalid or expired`; `PlaudCliClient`
   (`backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudCliClient.cs`) catches the resulting
   `PlaudAuthExpiredException`.
2. It calls `PlaudTokenRefresher` (`PlaudTokenRefresher.cs`), which:
   - Reads the current refresh token from `~/.plaud/tokens.json` on disk.
   - Calls `PlaudTokenRefreshClient` → Plaud OAuth refresh endpoint.
   - Validates the response: non-empty tokens, `expires_at` in the future. Throws if invalid — disk
     and KV are never overwritten with garbage.
   - Writes new token JSON to disk first (0600 permissions), then to Key Vault `Plaud--TokensJson`
     **best-effort** (a KV failure is logged, not thrown — disk already healed the running process).
   - A `SemaphoreSlim` serializes concurrent refreshes so overlapping CLI calls don't double-refresh.
3. `PlaudCliClient` retries the CLI call once. A second `AUTH_FAILED` (refresh token itself stale)
   propagates as `PlaudAuthExpiredException` and fires the Azure Monitor alert.
4. `PlaudTokenBootstrapper` re-seeds `~/.plaud/tokens.json` from Key Vault on the next container
   restart, so the KV-persisted token survives restarts.

The refresh HTTP client is registered unconditionally; Key Vault persistence is wired only when
`KeyVault:Uri` is set (production/staging). In local dev the refresher writes disk only.

**RBAC setup (once per env)** — still required so the app's managed identity can write the KV secret:
```bash
./scripts/grant-plaud-token-refresh-permission.sh stg
./scripts/grant-plaud-token-refresh-permission.sh stg --phase=cleanup  # after verified
```

**Rollback** — promote the prior KV secret version:
```bash
az keyvault secret list-versions --vault-name kv-heblo-prod --name Plaud--TokensJson -o table
az keyvault secret set --vault-name kv-heblo-prod --name Plaud--TokensJson \
    --value "$(az keyvault secret show --vault-name kv-heblo-prod --name Plaud--TokensJson \
        --version <prev-version-id> --query value -o tsv)"
az webapp restart -g rgHeblo -n heblo
```

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

From `@plaud-ai/cli` source inspection (`/opt/homebrew/bin/plaud`, the bundled Node script). The
request body **must be `application/x-www-form-urlencoded`** — sending JSON returns
`422 Unprocessable Entity`:

```
POST https://platform.plaud.ai/developer/api/oauth/third-party/access-token/refresh
Content-Type: application/x-www-form-urlencoded
Accept: application/json

refresh_token=<current_refresh_token>
```

Response shape (from CLI parsing — note `expires_in`, not `expires_at`):

```json
{
  "access_token": "...",
  "refresh_token": "...",   // may be omitted when only the access token rotates → reuse the old one
  "token_type": "bearer",
  "expires_in": 1209600     // relative seconds until expiry
}
```

The CLI (and our `PlaudTokenRefreshClient`) computes the stored `expires_at` as a **Unix millisecond**
timestamp: `now_ms + expires_in * 1000`. The `~/.plaud/tokens.json` file therefore stores
`expires_at` in milliseconds (13 digits), which is the format `PlaudTokenRefreshJob` validates and
re-serializes.

> **Open question:** Confirm Plaud's refresh-token hard TTL by inspecting `expires_in` and observing
> rotation over several days. The hard TTL appears to be ~30 days but is not officially documented.

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
