Now I have enough context. Writing the architecture review.

```markdown
# Architecture Review: Plaud CLI Auth Token Expiry Handling

## Skip Design: true

No UI/UX work — this is a backend reliability fix (token expiry detection, in-line OAuth refresh, structured telemetry, alerting, operator runbook).

## Architectural Fit Assessment

The change lives entirely inside `Anela.Heblo.Adapters.Plaud` and the existing Clean Architecture / vertical-slice layout. All the pieces required are already present:

- `IPlaudClient` (Application layer abstraction) — the public surface stays unchanged; behavior change only.
- `PlaudCliClient` (singleton, adapter implementation) — the single point that throws `PlaudAuthExpiredException`; the right place to insert detect → refresh → retry.
- `PlaudTokenRefreshClient` / `IPlaudTokenRefreshClient` — the OAuth refresh POST is already implemented and tested with `HttpMessageHandler` stubs.
- `PlaudTokenRefreshJob` (Hangfire `IRecurringJob`) — already wired and registered, just disabled by default. Owns the same disk + KV write-back flow we need.
- `PlaudTokenBootstrapper` (`IHostedService`) — already materialises `Plaud--TokensJson` from config into `~/.plaud/tokens.json` at startup so the CLI can read it.
- `ITelemetryService` (`Anela.Heblo.Xcc.Telemetry`) — established AI custom-event sink with `TrackBusinessEvent` / `TrackException` / `TrackMetric`. `PlaudCliClient` currently uses `ILogger` only; we add `ITelemetryService` here too.

**Key integration points / friction:**

1. **Token state today is split across three locations:** in-process `PlaudOptions.TokensJson` (snapshot at startup), disk `~/.plaud/tokens.json` (what the CLI actually reads on every shell-out), and `Plaud--TokensJson` in Key Vault (source-of-truth, only re-read on container restart). After an in-line refresh, **all three must be reconciled** or the next call will use a stale token.
2. **Lifetime mismatch:** `PlaudCliClient` is `Singleton`, `IPlaudTokenRefreshClient` is `Scoped`. A singleton cannot take a scoped dependency directly. We resolve this by elevating the refresh-client family to singleton (it's a stateless `HttpClient` wrapper).
3. **`PlaudCliClient` shells out to a CLI; it does not currently parse `expires_at` itself.** The expiry-check requirement (FR-2) forces `PlaudCliClient` to start owning a typed view of the cached tokens. Today it has no awareness of token contents at all — only `CliExecutablePath` and timeout. This is the largest behavioral shift in the change.
4. **No infrastructure-as-code in this repo.** Alerts (`Heblo-Plaud-AuthExpired`, action group `ag-heblo-ops`) live in Azure Portal only. New alerts (FR-4) are operational tasks, not code changes — captured in the runbook, not committed alongside C#.

The proposal fits the codebase cleanly. The biggest deliberate choice is introducing a small token-store abstraction (`IPlaudTokenStore`) used by both `PlaudCliClient` and the existing `PlaudTokenRefreshJob` so KV/disk reconciliation lives in one place.

## Proposed Architecture

### Component Overview

```
┌────────────────────────────────────────────────────────────────┐
│ Application: IPlaudClient consumers (MeetingTasks)             │
└──────────────────────────┬─────────────────────────────────────┘
                           │ ListRecentAsync / GetTranscriptAsync / ...
                           ▼
┌────────────────────────────────────────────────────────────────┐
│ PlaudCliClient (Singleton)                                     │
│   - in-memory PlaudTokens cache + SemaphoreSlim (single-flight)│
│   - EnsureFreshTokenAsync() before each RunCliAsync            │
│   - on AUTH_FAILED stderr: TryRefreshAsync + retry once        │
└─────┬──────────────────────┬──────────────────────┬────────────┘
      │ refresh              │ read/write tokens    │ telemetry
      ▼                      ▼                      ▼
┌────────────────┐  ┌────────────────────┐  ┌──────────────────┐
│ IPlaudToken    │  │ IPlaudTokenStore   │  │ ITelemetryService│
│ RefreshClient  │  │  - LoadAsync       │  │  (AI custom      │
│  (HTTP POST    │  │  - SaveAsync       │  │   events)        │
│   to Plaud)    │  │    (KV + disk)     │  │                  │
└────────────────┘  └─────────┬──────────┘  └──────────────────┘
                              │
                              ▼
                  ┌────────────────────────┐
                  │ SecretClient (Singleton)│
                  │ + ~/.plaud/tokens.json │
                  └────────────────────────┘
                              ▲
                              │ also used by
                  ┌────────────────────────┐
                  │ PlaudTokenRefreshJob   │  (Hangfire weekly)
                  │  unchanged contract,   │
                  │  now delegates to      │
                  │  IPlaudTokenStore      │
                  └────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Where the expiry check + refresh logic lives

**Options considered:**
- (a) Inside `PlaudCliClient.RunCliAsync` directly.
- (b) New decorator `RefreshingPlaudClient : IPlaudClient` that wraps `PlaudCliClient`.
- (c) New collaborator `IPlaudTokenManager` injected into `PlaudCliClient`, which owns the cache, the refresh semaphore, and the store.

**Chosen approach:** (c) — a thin `PlaudTokenManager : IPlaudTokenManager` that owns: the in-memory `PlaudTokens` snapshot, the `SemaphoreSlim`, "is this stale" computation, and orchestration of `IPlaudTokenRefreshClient` + `IPlaudTokenStore`.

**Rationale:** `PlaudCliClient` already has a non-trivial responsibility (process lifecycle, output parsing, AUTH_FAILED detection). Adding cache + single-flight + KV write-back inside it would push it past the global `<800 lines` / `<50 lines per function` guideline. A decorator (b) is appealing but cannot reactively handle the "I called the CLI and got AUTH_FAILED" path without itself parsing CLI errors, which leaks responsibilities back. Pattern (c) lets `PlaudCliClient.RunCliAsync` stay close to today: `await _tokenManager.EnsureFreshAsync(ct)` before each shell-out, and on `AUTH_FAILED` `await _tokenManager.ForceRefreshAsync(ct)` then retry once.

#### Decision 2: DI lifetimes for the new collaborators

**Options considered:**
- Match existing scoped `IPlaudTokenRefreshClient` and inject `IServiceScopeFactory` into the singleton `PlaudCliClient`.
- Elevate the refresh client + store + manager to singleton.

**Chosen approach:** Singleton for `IPlaudTokenRefreshClient`, `IPlaudTokenStore`, and `IPlaudTokenManager`. `SecretClient` and `HttpClient` (via `IHttpClientFactory`) are already designed for singleton ownership; the refresh client carries no per-request state.

**Rationale:** Single-flight refresh requires one shared `SemaphoreSlim` across all callers, which only works if the manager and its cache are singleton-scoped. Scope-bridging from a singleton consumer is more error-prone (lifetime leaks, captured scope). `PlaudTokenRefreshJob` (today registered as `Scoped<IRecurringJob>`) keeps its scoped registration; it depends on `IPlaudTokenRefreshClient` (now singleton), which is a valid direction.

#### Decision 3: Source-of-truth and write order

**Options considered:**
- KV-first, then disk; rollback disk on KV failure.
- Disk-first, then KV; warn on KV failure but keep going.
- Atomic two-phase write.

**Chosen approach:** **Disk-first, then KV**, mirroring the existing `PlaudTokenRefreshJob.ExecuteAsync`. In-memory cache is updated only after the disk write succeeds. KV-write failure is logged at `Warning`, emits `PlaudTokenRefreshKeyVaultWriteFailed`, but the request continues — the in-process and on-disk tokens are both fresh, so the running container is healthy until next restart.

**Rationale:** The Plaud CLI reads from disk on every invocation, so disk **must** be updated for the current call to succeed. KV is only consulted at next container start (via `PlaudOptions.TokensJson` config binding + `PlaudTokenBootstrapper`). Writing disk first matches FR-3's "in-process state still updated" acceptance criterion and the existing pattern in `PlaudTokenRefreshJob.cs:69-74`. Disk-write failure remains fatal (no point continuing — the CLI would still see the old token).

#### Decision 4: Reuse of `PlaudTokenRefreshJob` and its `DefaultIsEnabled` flag

**Options considered:**
- Flip `DefaultIsEnabled = true` in code as part of this PR.
- Leave the code flag at `false`; only flip it in the production Background Jobs admin UI (per FR-1, step 4).
- Delete the job entirely now that `PlaudCliClient` self-refreshes.

**Chosen approach:** Flip `DefaultIsEnabled = true` in code AND have the runbook confirm the admin-UI toggle. Keep the job — it is defence-in-depth per NFR-3.

**Rationale:** The original incident was caused exactly by the production admin-UI toggle being missed despite the code existing. Defaulting to enabled removes a foot-gun for any future environment provisioned from scratch. The runbook still must verify the production toggle because admin-UI overrides persist in the `IRecurringJobStatusChecker` store regardless of `DefaultIsEnabled`. Keeping the job is cheap and gives a second safety net against `PlaudCliClient` regressions.

#### Decision 5: Telemetry — `ITelemetryService` vs raw `TelemetryClient`

**Options considered:**
- Inject `Microsoft.ApplicationInsights.TelemetryClient` directly.
- Use the project's `ITelemetryService.TrackBusinessEvent` wrapper.

**Chosen approach:** `ITelemetryService.TrackBusinessEvent` for the four custom events (`PlaudTokenNearExpiry`, `PlaudTokenExpired`, `PlaudTokenRefreshed`, `PlaudTokenRefreshFailed`) plus `TrackException` for `PlaudAuthExpiredException` surfacing.

**Rationale:** Project-wide convention — every other adapter/job that emits custom events uses `ITelemetryService` (see `ProductExportDownloadJob`, `InvoiceClassificationJob`, `FlexiManufactureTemplateService`). It also gives free no-op behavior in non-production environments via `NoOpTelemetryService` (`PersistenceModule.cs:106`).

#### Decision 6: Token identifier in telemetry (NFR-2 compliance)

**Chosen approach:** A non-reversible 4-char HMAC suffix of the refresh token using a per-process random key (held in memory only; rotated on each process start). Emitted as the `tokenIdShort` property. The HMAC key never leaves memory.

**Rationale:** The spec requires a non-reversible identifier for cross-event correlation; a static hash (e.g., SHA-256 prefix) is technically reversible against a small candidate set, while a keyed HMAC is not. Per-process key rotation means correlation works inside a single deployment but a token cannot be tracked across deployments — acceptable given the events all fire within a process lifetime.

## Implementation Guidance

### Directory / Module Structure

All new code lives in `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/`:

```
Anela.Heblo.Adapters.Plaud/
├── PlaudCliClient.cs                  (modified — add EnsureFreshAsync + retry path)
├── PlaudOptions.cs                    (modified — add ExpiryBuffer, RefreshTimeout)
├── PlaudCredentialsOptions.cs         (NEW — typed options, validated at startup)
├── IPlaudTokenManager.cs              (NEW — abstraction used by PlaudCliClient)
├── PlaudTokenManager.cs               (NEW — owns cache + single-flight + telemetry)
├── IPlaudTokenStore.cs                (NEW — KV + disk read/write abstraction)
├── PlaudTokenStore.cs                 (NEW — reads/writes Plaud--TokensJson + ~/.plaud/tokens.json)
├── PlaudTelemetryEventNames.cs        (NEW — constants for the 4 event names)
├── PlaudTokenRefreshJob.cs            (modified — delegate KV/disk writes to IPlaudTokenStore;
│                                        flip DefaultIsEnabled = true)
├── PlaudAdapterServiceCollectionExtensions.cs   (modified — register new types as Singleton)
└── (others unchanged)
```

Tests in `backend/test/Anela.Heblo.Adapters.Plaud.Tests/`:

```
PlaudTokenManagerTests.cs              (NEW — covers the six FR-5 scenarios)
PlaudTokenStoreTests.cs                (NEW — fakes SecretClient + temp-dir disk)
PlaudCliClientRunTests.cs              (modified — add a near-expiry / refresh-retry case
                                        using a shim CLI that fails once, then succeeds)
```

New runbook: `docs/operations/plaud-token-rotation.md` (referenced from FR-4 alert payloads).

### Interfaces and Contracts

```csharp
namespace Anela.Heblo.Adapters.Plaud;

// Public interface stays as-is (Application layer):
//   Anela.Heblo.Application.Features.MeetingTasks.Services.IPlaudClient
// PlaudCliClient continues to implement it. Same method signatures.

public sealed class PlaudCredentialsOptions
{
    public const string SectionKey = "Plaud:Credentials";

    public string TokensJsonSecretName { get; init; } = "Plaud--TokensJson";
    public TimeSpan ExpiryBuffer { get; init; } = TimeSpan.FromHours(72);
    public TimeSpan RefreshTimeout { get; init; } = TimeSpan.FromSeconds(5);
}

internal interface IPlaudTokenManager
{
    // Called before each CLI invocation; refreshes if cached token is inside ExpiryBuffer.
    // No-op on the happy path (in-memory comparison only, no IO).
    Task EnsureFreshAsync(CancellationToken ct);

    // Called when the CLI returns AUTH_FAILED. Forces a refresh + writes through the store.
    // Single-flight: concurrent callers await the same refresh task.
    // Returns true on success; false means the runbook (FR-1) is required.
    Task<bool> ForceRefreshAsync(CancellationToken ct);
}

internal interface IPlaudTokenStore
{
    Task<PlaudTokens> LoadAsync(CancellationToken ct);

    // Disk-first then KV. Throws on disk failure. KV failure is signalled via
    // KeyVaultWriteFailed = true on the result so callers can emit the warning.
    Task<PlaudTokenSaveResult> SaveAsync(PlaudTokens tokens, CancellationToken ct);
}

public sealed record PlaudTokenSaveResult(bool KeyVaultWriteFailed, Exception? KeyVaultError);
```

Telemetry event contract (`ITelemetryService.TrackBusinessEvent`):

| Event name | Level | Properties |
|---|---|---|
| `PlaudTokenNearExpiry` | Warning | `expiresAt`, `bufferHours`, `tokenIdShort` |
| `PlaudTokenExpired` | Error | `expiresAt`, `tokenIdShort` |
| `PlaudTokenRefreshed` | Information | `expiresAt`, `tokenIdShort`, `triggeredBy` ∈ {`near-expiry`, `auth-failed-retry`} |
| `PlaudTokenRefreshFailed` | Error | `reason`, `tokenIdShort` (no exception payload — see Risks) |

`PlaudAuthExpiredException` continues to surface from `RunCliAsync` only when `ForceRefreshAsync` returns false OR a retried CLI invocation also returns `AUTH_FAILED`.

### Data Flow

**Happy path — fresh token:**
1. Application calls `IPlaudClient.ListRecentAsync(7)`.
2. `PlaudCliClient.RunCliAsync` calls `_tokenManager.EnsureFreshAsync(ct)`.
3. Manager compares cached `expiresAt` vs `now + ExpiryBuffer`. Inside buffer → return immediately (no IO).
4. `Process.Start` → CLI reads `~/.plaud/tokens.json` → success.
5. Output parsed and returned.

**Near-expiry path (proactive):**
1. Steps 1–2 as above.
2. `EnsureFreshAsync` detects `expiresAt ∈ (now, now + ExpiryBuffer]`.
3. Emits `PlaudTokenNearExpiry`. Acquires single-flight semaphore.
4. Calls `IPlaudTokenRefreshClient.RefreshAsync(refreshToken)` with `RefreshTimeout`.
5. On success: `IPlaudTokenStore.SaveAsync` writes disk + KV. Updates in-memory cache. Emits `PlaudTokenRefreshed{triggeredBy=near-expiry}`. Releases semaphore.
6. CLI invoked once; succeeds with fresh disk token.

**Reactive path (AUTH_FAILED at runtime):**
1. CLI invocation returns AUTH_FAILED stderr (today's exception trigger).
2. `PlaudCliClient` calls `_tokenManager.ForceRefreshAsync(ct)`.
3. If returns true → retry `RunCliAsync` **once**. On second AUTH_FAILED → throw `PlaudAuthExpiredException` (refresh token itself is dead; FR-1 runbook needed).
4. If returns false → emit `PlaudTokenRefreshFailed`, throw `PlaudAuthExpiredException`.

**Concurrent calls during refresh:** the second caller's `EnsureFreshAsync` or `ForceRefreshAsync` awaits the same semaphore; on entry, re-checks freshness and short-circuits if the prior refresh already completed.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Lifetime mismatch (singleton `PlaudCliClient` capturing scoped `IPlaudTokenRefreshClient` via `IServiceProvider`) leads to scope leak and `SecretClient` retention. | High | Elevate `IPlaudTokenRefreshClient`, `IPlaudTokenStore`, `IPlaudTokenManager` to **Singleton** at registration. Verified compatible with current `SecretClient` (already singleton) and `IHttpClientFactory`-managed `HttpClient` lifetimes. |
| Disk file (`~/.plaud/tokens.json`) not updated after KV refresh — CLI keeps reading old token and AUTH_FAILED storms. | High | `IPlaudTokenStore.SaveAsync` writes disk **before** KV (mirrors `PlaudTokenRefreshJob.cs:69-74`); in-memory cache only updated after disk write succeeds. Unit test asserts disk-before-KV ordering. |
| Refresh-token rotation: Plaud may issue a new refresh token on each refresh call. If KV write fails and process restarts, the old refresh token in KV is dead. | High | (a) Disk-first write means the process keeps working until restart. (b) KV-write failure emits `PlaudTokenRefreshKeyVaultWriteFailed` event + Warning log so the operator catches it before restart. (c) `PlaudTokenRefreshJob` weekly safety-net catches drift on subsequent week. |
| Two refresh paths (in-line in `PlaudCliClient` + weekly Hangfire job) racing on the same refresh token cause one to receive an already-rotated token. | Medium | Plaud's refresh endpoint is idempotent within a short window in practice; the weekly job runs at 04:00 Sunday when CLI traffic is minimal. If observed empirically, add a 30-second grace-window check in the job (read disk `expiresAt`; if `> now + 6 days`, skip). Not pre-emptively implemented. |
| Exception payload in `PlaudTokenRefreshFailed` event accidentally leaking refresh-token contents (e.g., included in response body that Plaud echoes back). | Medium | Custom event properties never include the exception message or response body — only a sanitised `reason` enum (`Timeout`, `HttpError`, `EmptyResponse`, `ExpiredInResponse`). Full exception goes through `TrackException` separately, which the project's `ITelemetryService` is already trusted to handle. Unit test asserts no token substring appears in any event property. |
| Single-flight `SemaphoreSlim` deadlocks if a refresh hangs past `RefreshTimeout`. | Medium | Use `SemaphoreSlim.WaitAsync(ct)` and wrap the refresh call in a `CancellationTokenSource` linked to `RefreshTimeout`. On timeout, release semaphore in `finally` and emit `PlaudTokenRefreshFailed{reason=Timeout}`. |
| Test for "live CI hits `platform.plaud.ai`" accidentally added later, rotating prod refresh token. | High | Add `[Trait("Category", "Live")]` convention check + a top-of-file comment in `PlaudTokenRefreshClientTests.cs` warning against live calls. CI already filters by traits — confirm in `dotnet test` invocation. |
| Existing `PlaudOptions.TokensJson` (config-bound string) and new `PlaudCredentialsOptions` overlap and confuse future readers. | Low | Keep `PlaudOptions.TokensJson` (only consumed by `PlaudTokenBootstrapper` at startup). `PlaudCredentialsOptions` is purely about timing/buffer behavior — no overlap. Document in XML doc comments and keep both classes deliberately separate. |
| Alerts (FR-4) live only in Azure Portal; configuration drift is invisible. | Low | Runbook captures the exact KQL queries, severities, and action group bindings for all three new alerts so they can be reconstructed manually. Not converted to IaC in this change (out of scope, codebase has no IaC today). |

## Specification Amendments

1. **NFR-2 / FR-3 clarification — `tokenIdShort` derivation.** The spec mentions "last 4 chars of HMAC" but does not specify the HMAC key source. Adopt: per-process random 32-byte key, generated once at adapter startup, kept in memory only, never logged. Document in `PlaudTokenManager` XML docs.
2. **FR-3 / NFR-3 clarification — write order.** Specification says "KV write fails after a successful Plaud refresh, the new token is still used in-process." Clarify the disk file is also updated **before** KV: write order is `(1) disk, (2) in-memory cache, (3) KV`. KV failure is non-fatal. Disk failure is fatal and bubbles `PlaudTokenRefreshFailed{reason=DiskWriteFailed}` + `PlaudAuthExpiredException`. This matches the existing job and is required for the CLI to see the fresh token.
3. **FR-3 retry policy clarification.** "Retries once with the refreshed credentials" — define "retry": after a successful `ForceRefreshAsync`, `PlaudCliClient` re-invokes the CLI **with the same arguments**, no additional backoff. If the second invocation also returns AUTH_FAILED, surface `PlaudAuthExpiredException` immediately (no further retries).
4. **FR-5 add a new test case.** "Disk-write failure" — token store fails to write `~/.plaud/tokens.json` (e.g., disk full). Assert `PlaudTokenRefreshFailed{reason=DiskWriteFailed}` event, caller sees `PlaudAuthExpiredException`, in-memory cache is **not** updated (so the next call retries the refresh rather than silently using a token the CLI cannot read).
5. **FR-1 runbook addition.** Add a step "verify `PlaudTokenRefreshJob` `DefaultIsEnabled` is `true` in code AND the production admin-UI toggle is enabled." Distinguishes the two flips.
6. **Decision 4 / `PlaudTokenRefreshJob.cs:22`.** Specification leaves the `DefaultIsEnabled` code change "to the implementer" — promote to a definitive change in this PR: flip to `true`. Removes the foot-gun for future provisioning.

## Prerequisites

Before merging:
- None — all dependencies (`SecretClient`, `IPlaudTokenRefreshClient`, `ITelemetryService`, `IRecurringJobStatusChecker`, options binding) are already registered in DI.

Before declaring the production incident resolved (operator actions; tracked in the runbook):
1. Run `plaud login` locally and obtain fresh `{access_token, refresh_token, expires_at}` JSON.
2. `az keyvault secret set --vault-name kv-heblo-prod --name "Plaud--TokensJson" --value '<json-blob>'`.
3. Restart the Heblo Azure Web App (App Service → Restart).
4. **Enable `plaud-token-refresh` Hangfire job in the production Background Jobs admin UI** — this is the load-bearing step that prevents recurrence on the next ~30-day cycle.
5. Confirm `PlaudAuthExpiredException` count → 0 within 15 minutes in App Insights.
6. Manually create the two new alerts (`PlaudTokenNearExpiry` Sev 3, `PlaudTokenRefreshFailed` Sev 2) in Azure Portal, both wired to `ag-heblo-ops`, with the new runbook URL in the payload. Verify the existing `Heblo-Plaud-AuthExpired` rule payload also references the new runbook.

No DB migrations, no infrastructure provisioning, no new secrets, no config schema additions in App Service settings.
```