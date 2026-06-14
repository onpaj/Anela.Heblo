# Specification: Plaud CLI Auth Token Expiry Handling

## Summary
The Plaud CLI session token used by `PlaudCliClient` has expired in production, producing a surge of `PlaudAuthExpiredException` errors (18 occurrences on 2026-06-12 after near-zero in the prior 5 days). This spec covers immediate operational remediation (re-auth + KV rotation), and a longer-term fix to detect and refresh expired Plaud credentials proactively so manual intervention is no longer required at each expiry cycle.

## Background
`Anela.Heblo.Adapters.Plaud.PlaudCliClient.RunCliAsync` invokes the Plaud CLI using a session token stored in Azure Key Vault (`kv-heblo-prod`). The token has a finite lifetime; when it expires, every trigger that calls `RunCliAsync` throws `PlaudAuthExpiredException` and the operation fails immediately because no retry / refresh path exists in the call site.

Telemetry evidence:
- 17 occurrences over P7D ending 2026-06-12T15:12Z (weekly digest snapshot).
- 18 occurrences on 2026-06-12 alone (more fired during the same triage session).
- Single `problemId`: `Anela.Heblo.Adapters.Plaud.PlaudAuthExpiredException at Anela.Heblo.Adapters.Plaud.PlaudCliClient+<RunCliAsync>d__7.MoveNext`.
- No PRs in the 7-day window touched `PlaudCliClient` or Plaud credentials — change-correlation rules out a code regression. Step-change pattern matches a token-expiry event.

The exception is actively occurring, meaning every downstream consumer of `PlaudCliClient` is currently broken in production. Until the token is rotated, all Plaud-dependent flows fail.

## Functional Requirements

### FR-1: Immediate production remediation (manual operator runbook)
Restore Plaud functionality in production by re-authenticating the Plaud CLI and rotating the resulting credential in Azure Key Vault.

**Acceptance criteria:**
- A documented runbook exists describing: (1) how to re-authenticate the Plaud CLI locally, (2) the exact KV secret name(s) to update via `az keyvault secret set --vault-name kv-heblo-prod --name "<name>" --value "<value>"`, and (3) how to verify the new secret is picked up (app restart / KV reload behavior).
- After executing the runbook against `kv-heblo-prod`, new `RunCliAsync` invocations succeed.
- `PlaudAuthExpiredException` count in telemetry drops to zero within 15 minutes of the rotation.
- The runbook explicitly forbids storing the Plaud secret in App Settings (per CLAUDE.md: all secrets go to Key Vault, separator is `--`).

### FR-2: Proactive token-expiry detection in `PlaudCliClient`
`PlaudCliClient` must detect that the current Plaud session token is expired or near-expiry **before** invoking the CLI, so the failure mode becomes a structured, actionable signal instead of an exception thrown from the middle of `RunCliAsync`.

**Acceptance criteria:**
- The client checks token validity (e.g., decoded expiry claim, cached `expires_at`, or a lightweight `whoami`-style probe) before each CLI invocation, or at startup + on a TTL-based cadence.
- When the token is expired or within a configurable expiry buffer (default: 24 hours before expiry), the client logs a structured warning (`PlaudTokenNearExpiry` / `PlaudTokenExpired`) including the expiry timestamp, and emits an Application Insights custom event so the existing telemetry-anomaly routine surfaces it.
- The detection path does not require a successful CLI call to discover the expiry.
- The token-expiry buffer is configurable via app configuration (bound to a typed options class — not magic numbers).

### FR-3: Automatic token refresh (where supported by Plaud)
If the Plaud authentication model supports refresh tokens or a non-interactive re-auth flow, `PlaudCliClient` must attempt automatic refresh when expiry is detected, write the new token back to Key Vault, and retry the original operation transparently.

**Acceptance criteria:**
- On detected expiry, the client attempts a single non-interactive refresh against the Plaud API.
- A successful refresh updates the KV secret (using the managed identity already configured for KV access) and updates the in-process cached token.
- The original `RunCliAsync` call retries once with the refreshed token; the caller observes success.
- Failed refresh attempts emit a structured `PlaudTokenRefreshFailed` event and surface `PlaudAuthExpiredException` to the caller as today, so the FR-1 runbook remains the fallback.
- Concurrent invocations during a refresh do not trigger multiple simultaneous refreshes (single-flight / mutex).
- **Assumption (see Open Questions):** Plaud supports a non-interactive refresh path. If it does not, FR-3 collapses into FR-4 only.

### FR-4: Alerting on Plaud auth failures
The team must be notified when Plaud authentication is failing in production, without waiting for the weekly telemetry digest.

**Acceptance criteria:**
- An Application Insights alert fires when `PlaudAuthExpiredException` occurrences exceed a threshold (default: 3 occurrences within 15 minutes) in production.
- Alert routes to the existing notification channel used for production incidents.
- Alert payload includes `problemId`, count, and a link to the runbook from FR-1.
- A separate, lower-severity alert fires on `PlaudTokenNearExpiry` warnings (FR-2) so manual rotation can be scheduled before failures occur if FR-3 is unavailable or fails.

### FR-5: Regression test coverage
Unit and integration tests cover the new expiry-detection, refresh, and retry paths so future changes do not silently regress the safety net.

**Acceptance criteria:**
- Unit tests for `PlaudCliClient` cover: valid token (no refresh), near-expiry token (warning emitted, refresh attempted if FR-3 active), expired token with successful refresh (one retry, caller sees success), expired token with failed refresh (caller sees `PlaudAuthExpiredException`), concurrent invocations during refresh (single refresh).
- Integration test (or contract test against a Plaud sandbox if available — see Open Questions) verifies the refresh round-trip against a real or mocked Plaud auth endpoint.
- Tests run as part of the existing `dotnet test` suite in PR CI.

## Non-Functional Requirements

### NFR-1: Performance
- Token validity check adds at most 5 ms to `RunCliAsync` invocations in the happy path (no network call when a cached expiry timestamp is available).
- A successful refresh round-trip completes within 5 seconds; failure is surfaced within 10 seconds.

### NFR-2: Security
- Plaud session tokens and refresh tokens are stored **only** in `kv-heblo-prod` (production) and `kv-heblo-stg` (staging); never in app settings, environment variables, source code, logs, or telemetry payloads.
- Structured log / telemetry events related to token expiry must not include the token value — only the expiry timestamp, event type, and a non-reversible identifier if needed for correlation.
- KV writes use the existing managed identity; no additional credentials are introduced.
- Secret name uses the KV `--` separator convention (e.g., `Plaud--SessionToken`, `Plaud--RefreshToken`).

### NFR-3: Reliability
- The token-refresh path is idempotent: repeated failed refreshes do not corrupt the KV-stored secret.
- If KV write fails after a successful Plaud refresh, the new token is still used in-process for the current request, and the next process restart will re-discover the (still valid) token via the existing refresh flow.

### NFR-4: Observability
- All new code paths emit structured logs at appropriate levels: `Information` for successful refresh, `Warning` for near-expiry, `Error` for refresh failure, and `Error` for `PlaudAuthExpiredException` reaching the caller.
- Application Insights custom events: `PlaudTokenRefreshed`, `PlaudTokenNearExpiry`, `PlaudTokenRefreshFailed`, with consistent property names so the existing telemetry-anomaly routine can correlate.

## Data Model
No persistent domain entities are added. Relevant in-memory / configuration types:

- **`PlaudCredentialsOptions`** (typed config, bound from configuration):
  - `SessionTokenSecretName: string` (e.g., `Plaud--SessionToken`)
  - `RefreshTokenSecretName: string?` (nullable; absent if Plaud does not support refresh)
  - `ExpiryBuffer: TimeSpan` (default 24 hours)
  - `RefreshTimeout: TimeSpan` (default 5 seconds)
- **In-memory cached token state** inside `PlaudCliClient`:
  - `currentToken: string`
  - `expiresAt: DateTimeOffset`
  - `refreshSemaphore: SemaphoreSlim` (single-flight refresh)

Secrets stored in Key Vault:
- `Plaud--SessionToken` (existing or to-be-renamed; see Open Questions for current name)
- `Plaud--RefreshToken` (new, if FR-3 applies)

## API / Interface Design

No new public HTTP endpoints. Changes are internal to the Plaud adapter.

**`IPlaudCliClient` (existing interface, behavior change only):**
- `RunCliAsync(...)` continues to throw `PlaudAuthExpiredException` only when refresh is unavailable or has failed. Successful refresh is transparent to callers — same return type, same signature.

**Internal additions inside `Anela.Heblo.Adapters.Plaud`:**
- `IPlaudTokenStore` — abstraction over the KV-backed token read/write so it can be unit-tested with a fake.
- `IPlaudAuthRefresher` — abstraction over the Plaud refresh API call.
- Both are registered in DI alongside the existing `PlaudCliClient` registration.

**Operator interface (FR-1 runbook):**
- A markdown runbook in `docs/operations/plaud-token-rotation.md` (new file) referenced from the FR-4 alert payload.

## Dependencies
- **Azure Key Vault (`kv-heblo-prod`, `kv-heblo-stg`)** — secret storage and rotation target. Managed identity for KV access already provisioned.
- **Plaud authentication API** — source of refresh capability. Exact endpoint and refresh semantics need confirmation (see Open Questions).
- **Application Insights** — telemetry sink and alert host. Existing `telemetry-anomaly` routine already consumes the exceptions table this work feeds.
- **`PlaudCliClient`** (existing) — the file being modified.
- **Existing DI / options binding infrastructure** — used to register `PlaudCredentialsOptions`, `IPlaudTokenStore`, `IPlaudAuthRefresher`.

## Out of Scope
- Replacing the Plaud CLI with a direct HTTP integration.
- Multi-tenant Plaud accounts (the project uses a single Plaud account).
- Retrying transient (non-auth) Plaud CLI failures — this spec covers auth expiry only.
- Migrating Plaud secrets from `kv-heblo-prod` to a different vault.
- Backfilling historical telemetry for past auth-expiry incidents.
- Changes to staging Plaud credentials beyond what is needed for the integration test in FR-5.

## Open Questions
1. **Does the Plaud authentication model support a non-interactive refresh token flow, or is every re-auth interactive?** If interactive-only, FR-3 is not implementable and FR-4's alerting becomes the primary mitigation. Assumption made: a refresh path exists.
2. **What is the current KV secret name for the Plaud session token in `kv-heblo-prod`?** The runbook (FR-1) and config defaults need the exact existing name; renaming may require a one-time migration.
3. **What is the typical Plaud session token lifetime?** Required to choose a sensible default for `ExpiryBuffer` and the FR-4 alert threshold.
4. **Is there a Plaud sandbox or staging environment usable for the FR-5 integration test, or must the test mock the Plaud auth endpoint?**
5. **Which Application Insights alert channel should FR-4 route to** — the same channel as the existing weekly telemetry-anomaly digest, or a higher-urgency channel (since the failure is active in production)?
6. **Should the immediate FR-1 rotation be performed before or after the FR-2/FR-3 code lands?** Recommendation: rotate immediately to restore production, then ship the code fix; confirm this sequencing.

## Status: HAS_QUESTIONS