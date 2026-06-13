# Specification: Plaud CLI Auth Token Expiry Handling

## Summary
The Plaud refresh token used by `PlaudCliClient` has expired in production, producing a surge of `PlaudAuthExpiredException` errors (18 occurrences on 2026-06-12 after near-zero in the prior 5 days). Root cause: the existing `plaud-token-refresh` Hangfire job is built but ships with `DefaultIsEnabled = false`, so production has been silently drifting toward the ~30-day refresh-token TTL. This spec covers immediate operational remediation (rotate the `Plaud--TokensJson` secret + enable the existing job), and a longer-term in-line refresh path in `PlaudCliClient` so the system no longer depends on a single weekly cron firing successfully.

## Background
`Anela.Heblo.Adapters.Plaud.PlaudCliClient.RunCliAsync` invokes the Plaud CLI using credentials stored in Azure Key Vault as a single JSON secret `Plaud--TokensJson` (`{ "access_token": "...", "refresh_token": "...", "expires_at": <unix-seconds> }`). The Plaud OAuth model uses a short-lived `access_token` (the CLI auto-refreshes it on every call) and a long-lived `refresh_token` whose observed hard TTL is approximately **30 days**. When the refresh token expires, every `RunCliAsync` invocation throws `PlaudAuthExpiredException`.

A non-interactive refresh path already exists:
- `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudTokenRefreshClient.cs` POSTs the current refresh token to `https://platform.plaud.ai/developer/api/oauth/third-party/access-token/refresh` and persists the new tuple to `Plaud--TokensJson` in Key Vault.
- A weekly Hangfire job `plaud-token-refresh` (cron `0 4 * * 0`) wraps the client.
- The job has `DefaultIsEnabled = false` and was never turned on in production — this is why production drifted into the expired state despite the refresh capability existing.

Telemetry evidence:
- 17 occurrences over P7D ending 2026-06-12T15:12Z (weekly digest snapshot); 18 on 2026-06-12 alone.
- Single `problemId`: `Anela.Heblo.Adapters.Plaud.PlaudAuthExpiredException at Anela.Heblo.Adapters.Plaud.PlaudCliClient+<RunCliAsync>d__7.MoveNext`.
- No PRs in the 7-day window touched `PlaudCliClient` or Plaud credentials — change-correlation rules out a code regression. Step-change pattern matches a refresh-token expiry event.
- The existing alert `Heblo-Plaud-AuthExpired` is wired to the `ag-heblo-ops` action group (email `ondra@anela.cz`).

The exception is actively occurring, meaning every downstream consumer of `PlaudCliClient` is currently broken in production. Until the refresh token is rotated and the existing job is enabled, the system will re-enter this state on every ~30-day cycle.

## Functional Requirements

### FR-1: Immediate production remediation (manual operator runbook)
Restore Plaud functionality in production by re-authenticating the Plaud CLI and rotating the `Plaud--TokensJson` secret in Azure Key Vault, then enabling the existing weekly refresh job so this incident does not recur.

**Acceptance criteria:**
- A runbook exists at `docs/operations/plaud-token-rotation.md` describing, in order:
  1. Run `plaud login` locally to obtain a fresh `access_token` + `refresh_token` + `expires_at` tuple.
  2. Update the single secret via `az keyvault secret set --vault-name kv-heblo-prod --name "Plaud--TokensJson" --value '<json-blob>'`. The value is the full JSON object — not two separate secrets.
  3. Restart the `Heblo` Azure Web App so Key Vault re-loads the secret at startup.
  4. **Enable the `plaud-token-refresh` Hangfire job in the production Background Jobs admin UI.** This is the critical step that prevents recurrence — the job is `DefaultIsEnabled = false` today, which is the root cause of this incident.
  5. Verify `PlaudAuthExpiredException` count in App Insights drops to zero within 15 minutes.
- The runbook explicitly forbids storing the Plaud secret in App Settings (per CLAUDE.md: all secrets go to Key Vault, separator is `--`).
- The runbook is linked from the FR-4 alert payload.

### FR-2: Proactive token-expiry detection in `PlaudCliClient`
`PlaudCliClient` must detect that the current Plaud refresh token is expired or near-expiry **before** invoking the CLI, so the failure mode becomes a structured, actionable signal instead of an exception thrown from the middle of `RunCliAsync`.

**Acceptance criteria:**
- Before each CLI invocation, the client checks the cached `expires_at` from `Plaud--TokensJson`. No additional network call is required on the happy path.
- When the refresh token is within `ExpiryBuffer` of expiry (default: **72 hours** — chosen to leave one full weekly-job retry window before hard expiry), the client logs a structured `Warning` and emits an Application Insights custom event `PlaudTokenNearExpiry` with properties `expiresAt`, `bufferHours`, and a non-reversible token identifier (e.g., last 4 chars of an HMAC of the token).
- When the refresh token is already expired, the client emits `PlaudTokenExpired` at `Error` level before attempting refresh (FR-3).
- The expiry buffer is configurable via `PlaudCredentialsOptions.ExpiryBuffer` — bound to a typed options class, not a magic number.
- Token values are never written to logs or telemetry payloads.

### FR-3: In-line automatic refresh in `PlaudCliClient`
When expiry is detected (FR-2), `PlaudCliClient` must attempt an in-line non-interactive refresh against the existing Plaud OAuth refresh endpoint, write the new tuple back to `Plaud--TokensJson` in Key Vault, and retry the original operation transparently. This hardens the system against the case where the weekly `plaud-token-refresh` Hangfire job fails or has not yet run.

**Acceptance criteria:**
- On detected expiry (or near-expiry), the client invokes the existing `PlaudTokenRefreshClient` (or a shared abstraction) to call `https://platform.plaud.ai/developer/api/oauth/third-party/access-token/refresh` with the current `refresh_token`.
- A successful refresh updates `Plaud--TokensJson` via the existing managed-identity-backed `SecretClient` and updates the in-process cached tuple.
- The original `RunCliAsync` call retries **once** with the refreshed credentials; the caller observes success and the existing call signature is unchanged.
- Failed refresh attempts emit a structured `PlaudTokenRefreshFailed` event (Error level) and surface `PlaudAuthExpiredException` to the caller as today — the FR-1 runbook remains the fallback for the case where the refresh token itself is dead.
- Concurrent invocations during a refresh do not trigger multiple simultaneous refresh calls: a `SemaphoreSlim`-backed single-flight ensures one refresh, with other callers awaiting its result.
- A successful in-line refresh emits `PlaudTokenRefreshed` (Information level) with `expiresAt` and a non-reversible identifier.
- If the KV write fails after a successful Plaud refresh, the new token is still used in-process for the current request; the failure is logged at `Warning` so the next process restart (which re-reads KV) does not silently regress.

### FR-4: Alerting on Plaud auth failures
The team must be notified when Plaud authentication is failing in production, without waiting for the weekly telemetry digest.

**Acceptance criteria:**
- An Application Insights alert fires when `PlaudAuthExpiredException` occurrences are **count > 0 within a 15-minute window, evaluated every 5 minutes**, in production. This matches the existing `Heblo-Plaud-AuthExpired` rule cadence.
- Severity = **2 (Warning)** for `PlaudAuthExpiredException`.
- Alert routes to the existing action group **`ag-heblo-ops`** (email `ondra@anela.cz`). No new channels are created.
- Alert payload includes `problemId`, count, and a link to `docs/operations/plaud-token-rotation.md`.
- A separate alert with severity **3 (Informational)** fires on `PlaudTokenNearExpiry` so manual rotation can be scheduled before hard failure — routed to the same `ag-heblo-ops` group.
- A third alert with severity **2** fires on `PlaudTokenRefreshFailed` events, indicating the in-line refresh failed and the FR-1 runbook is required.

### FR-5: Regression test coverage
Unit tests cover the new expiry-detection, refresh, and retry paths so future changes do not silently regress the safety net.

**Acceptance criteria:**
- Unit tests for `PlaudCliClient` cover:
  - Valid token, well outside `ExpiryBuffer` → no refresh, no warning, CLI invoked once.
  - Near-expiry token (inside buffer, not yet expired) → `PlaudTokenNearExpiry` event emitted, refresh attempted, CLI invoked with new token.
  - Expired token with successful refresh → one retry, caller sees success, `PlaudTokenRefreshed` event emitted, `Plaud--TokensJson` updated.
  - Expired token with failed refresh → `PlaudTokenRefreshFailed` event emitted, caller sees `PlaudAuthExpiredException`.
  - Concurrent invocations during a refresh → single refresh call (verified via mock call count).
  - KV write failure after successful Plaud refresh → in-process state still updated, warning logged, caller sees success.
- Integration coverage uses a fake `HttpMessageHandler` mocking the Plaud refresh endpoint — the pattern already established in `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudTokenRefreshClientTests.cs`. **No CI test ever calls the live `platform.plaud.ai` endpoint** (would rotate the production refresh token and break the running app — Plaud has no sandbox).
- All new tests run as part of the existing `dotnet test` suite in PR CI.

## Non-Functional Requirements

### NFR-1: Performance
- Token validity check adds at most 5 ms to `RunCliAsync` invocations in the happy path (in-memory comparison against cached `expires_at`; no network call).
- A successful refresh round-trip completes within `RefreshTimeout` (default 5 seconds); failure surfaces within 10 seconds end-to-end.

### NFR-2: Security
- Plaud credentials are stored **only** in the single secret `Plaud--TokensJson` in `kv-heblo-prod` (production) and `kv-heblo-stg` (staging). Never in app settings, environment variables, source code, logs, or telemetry payloads.
- Structured log / telemetry events related to token expiry must not include the `access_token` or `refresh_token` values — only `expiresAt`, event type, and a non-reversible identifier (e.g., last 4 chars of HMAC) if needed for correlation across events.
- KV writes use the existing managed identity already configured for `SecretClient`; no additional credentials are introduced.

### NFR-3: Reliability
- The token-refresh path is idempotent: repeated failed refreshes do not corrupt `Plaud--TokensJson`. Writes only occur after a successful response from the Plaud refresh endpoint.
- If the KV write fails after a successful Plaud refresh, the new token is still used in-process for the current request, and the next process restart will re-discover credentials via the standard configuration binding.
- The in-line refresh in `PlaudCliClient` (FR-3) is independent of the weekly `plaud-token-refresh` Hangfire job — either path is sufficient on its own to keep the system healthy. Defence in depth.

### NFR-4: Observability
- All new code paths emit structured logs at appropriate levels: `Information` for successful refresh (`PlaudTokenRefreshed`), `Warning` for near-expiry (`PlaudTokenNearExpiry`) and KV write failures, `Error` for hard expiry (`PlaudTokenExpired`) and refresh failure (`PlaudTokenRefreshFailed`), and `Error` for `PlaudAuthExpiredException` reaching the caller.
- Application Insights custom events use consistent property names (`expiresAt`, `bufferHours`, `tokenIdShort`) so the existing telemetry-anomaly routine can correlate them.

## Data Model
No persistent domain entities are added. Relevant in-memory / configuration types:

- **`PlaudCredentialsOptions`** (typed config, bound from configuration; extends/augments existing `PlaudOptions`):
  - `TokensJsonSecretName: string` (default `"Plaud--TokensJson"`)
  - `ExpiryBuffer: TimeSpan` (default `TimeSpan.FromHours(72)`)
  - `RefreshTimeout: TimeSpan` (default `TimeSpan.FromSeconds(5)`)
  - (No separate `RefreshTokenSecretName` — there is one secret containing both tokens as JSON.)
- **In-memory cached token state** inside `PlaudCliClient`:
  - `accessToken: string`
  - `refreshToken: string`
  - `expiresAt: DateTimeOffset`
  - `refreshSemaphore: SemaphoreSlim` (single-flight refresh)

Secret stored in Key Vault (existing, no migration required):
- `Plaud--TokensJson` — JSON blob `{ "access_token": "...", "refresh_token": "...", "expires_at": <unix-seconds> }`.

## API / Interface Design

No new public HTTP endpoints. Changes are internal to the Plaud adapter.

**`IPlaudCliClient` (existing interface, behavior change only):**
- `RunCliAsync(...)` continues to throw `PlaudAuthExpiredException` **only** when both the cached token is expired AND the in-line refresh fails. Successful in-line refresh is transparent — same return type, same signature.

**Internal additions inside `Anela.Heblo.Adapters.Plaud`:**
- Reuse the existing `PlaudTokenRefreshClient` for the HTTP refresh call (do not duplicate the OAuth POST logic).
- Introduce `IPlaudTokenStore` — a thin abstraction over `SecretClient.GetSecretAsync` / `SetSecretAsync` for `Plaud--TokensJson`, so it can be unit-tested with a fake. The existing `PlaudTokenRefreshJob` should be refactored to use this same abstraction to keep KV access in one place.
- Both `PlaudTokenRefreshClient` and `IPlaudTokenStore` are registered in DI alongside the existing `PlaudCliClient` registration.

**Operator interface (FR-1 runbook):**
- New file `docs/operations/plaud-token-rotation.md`, referenced from the FR-4 alert payloads.

**Existing Hangfire job:**
- `plaud-token-refresh` (weekly cron `0 4 * * 0`) — **no code change**, but its `DefaultIsEnabled` value will be flipped to `true` going forward (or the production instance toggled via the Background Jobs admin UI per FR-1, step 4). Decision on whether to also flip the default in code is left to the implementer based on staging behavior.

## Dependencies
- **Azure Key Vault (`kv-heblo-prod`, `kv-heblo-stg`)** — secret storage for `Plaud--TokensJson`. Managed identity for KV access already provisioned.
- **Plaud OAuth refresh endpoint** — `https://platform.plaud.ai/developer/api/oauth/third-party/access-token/refresh`. Already exercised by `PlaudTokenRefreshClient`. No sandbox available.
- **Application Insights** — telemetry sink and alert host. Existing `telemetry-anomaly` routine and `Heblo-Plaud-AuthExpired` alert rule are reused.
- **Action group `ag-heblo-ops`** — existing notification channel (email `ondra@anela.cz`). All new alerts route here.
- **`PlaudCliClient`** (existing) — the file being modified for FR-2/FR-3.
- **`PlaudTokenRefreshClient`** and **`PlaudTokenRefreshJob`** (existing) — reused; the latter must be enabled in production as part of FR-1.
- **Existing DI / options binding infrastructure** — used to register `PlaudCredentialsOptions` and `IPlaudTokenStore`.

## Out of Scope
- Replacing the Plaud CLI with a direct HTTP integration.
- Multi-tenant Plaud accounts (the project uses a single Plaud account).
- Retrying transient (non-auth) Plaud CLI failures — this spec covers auth expiry only.
- Migrating Plaud secrets to a different vault or splitting `Plaud--TokensJson` into separate secrets.
- Backfilling historical telemetry for past auth-expiry incidents.
- Integration tests that hit the live `platform.plaud.ai` refresh endpoint from CI (would rotate the production refresh token).
- Re-implementing the OAuth refresh HTTP call — reuse the existing `PlaudTokenRefreshClient`.
- Building a sandbox/mock Plaud server beyond the `HttpMessageHandler` fakes already used in tests.

## Open Questions
None.

## Status: COMPLETE