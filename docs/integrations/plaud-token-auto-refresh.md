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
5. **Disk→KV sync on every successful call.** The Plaud CLI silently rotates the on-disk refresh
   token during a *normal* (non-`AUTH_FAILED`) call, which the reactive path never sees. After each
   successful CLI call `PlaudCliClient` calls `IPlaudTokenRefresher.SyncToKeyVaultAsync`, which
   mirrors `~/.plaud/tokens.json` to KV `Plaud--TokensJson` **only when it changed** (best-effort).
   This keeps Key Vault current so a restart never re-seeds a stale token — the fix for the
   restart-stale-token problem below.

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

## Root Cause of the Restart-Stale-Token Problem (fixed)

`PlaudTokenBootstrapper` (`backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudTokenBootstrapper.cs`)
writes `~/.plaud/tokens.json` from `PlaudOptions.TokensJson` on every container start.

**Config precedence:** `Program.cs` adds environment variables first (via `CreateBuilder`), then layers
`AddAzureKeyVault` on top (only user-secrets and command-line are re-added after KV). So the **Key Vault
secret `Plaud--TokensJson` overrides the App Service env var `Plaud__TokensJson`** — Key Vault is the
effective source of truth on startup. Editing the env var has no effect; it should not exist (secrets
live in Key Vault only) and has been removed.

The Plaud CLI auto-refreshes (rotates) its refresh token on disk during normal polling. Previously those
rotations were **never mirrored to Key Vault** (only the reactive `PlaudTokenRefresher` wrote KV, and only
on `AUTH_FAILED`), so the KV secret froze at its seeded value. A container restart then re-seeded that
now-invalidated refresh token, and every subsequent CLI call failed `[AUTH_FAILED]` → refresh returned
`{"detail":"REFRESH_TOKEN_INVALID"}` (HTTP 401) before it could heal KV → permanent wedge.

**Fix (implemented):** `SyncToKeyVaultAsync` (step 5 in *How It Works*) mirrors the CLI's on-disk token
rotations to KV after every successful call, so the KV secret always reflects the live token and a
restart re-seeds a valid one. `PlaudPollingJob` keeps
`[AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]` so a genuinely dead
token surfaces `PlaudAuthExpiredException` immediately (no 10× retry flood) and fires the Azure Monitor
alert `Heblo-Plaud-AuthExpired` within 5 minutes.

## Recovery Runbook (refresh token dead → `REFRESH_TOKEN_INVALID`)

If the stored refresh token dies (e.g. aged past Plaud's hard TTL while polling was paused), the token
must be re-minted and written to **Key Vault** — not the App Service env var (which is overridden). For
production (`kv-heblo-prod` / app `heblo`); use `kv-heblo-stg` / `heblo-test` for staging:

```bash
plaud login                                   # interactive — mints a fresh token pair
cat ~/.plaud/tokens.json                       # verify access_token, refresh_token, expires_at (13-digit ms)

az keyvault secret set --vault-name kv-heblo-prod --name "Plaud--TokensJson" \
    --value "$(cat ~/.plaud/tokens.json)"      # write to the source of truth

az webapp config appsettings delete -g rgHeblo -n heblo --setting-names Plaud__TokensJson  # remove stale env var (if present)
az webapp restart -g rgHeblo -n heblo          # re-seed disk from fresh KV

# Verify: no new auth failures, and KV 'updated' starts advancing as polling rotates + syncs the token
az monitor app-insights query --app aiHeblo -g rgHeblo \
  --analytics-query "exceptions | where type endswith 'PlaudAuthExpiredException' | where timestamp > ago(15m) | count"
az keyvault secret show --vault-name kv-heblo-prod --name Plaud--TokensJson --query attributes.updated -o tsv
```

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

## Proposed Design (historical — superseded by the reactive implementation above)

> This section is the original design sketch. It has been **implemented differently**: refresh is
> reactive (no standalone `plaud-token-refresh` job), KV is kept current via `SyncToKeyVaultAsync`
> (see *How It Works* step 5), and the `Plaud__TokensJson` App Service env var has been removed —
> Key Vault `Plaud--TokensJson` is the source of truth. Kept for context only.

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
