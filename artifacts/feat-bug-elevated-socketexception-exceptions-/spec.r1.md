# Specification: Investigate and Mitigate Elevated SocketException Errors

## Summary
Production telemetry shows a 5x spike in `System.Net.Sockets.SocketException` occurrences over the last 24 hours (24 events vs. a 7-day baseline of 4.86/day), with the sample message `The operation was canceled.` This specification covers the investigation, diagnostic instrumentation, root-cause identification, and remediation of the underlying socket failures so that exception rates return to baseline and outbound network calls degrade gracefully.

## Background
The Anela Heblo backend (.NET 8) makes outbound HTTP and socket calls to multiple external integrations (Shoptet REST API, MCP server, ABRA Flexi, Azure services, etc.). `SocketException` with the message *"The operation was canceled."* typically indicates one of:

- A `CancellationToken` (request abort, timeout, or shutdown) firing mid-socket-operation.
- An `HttpClient` timeout or `SocketsHttpHandler` connection-pool issue (e.g., `PooledConnectionLifetime` expiry mid-flight).
- Server-side connection resets from a remote dependency, surfaced as a wrapped socket cancellation.
- Application shutdown / IIS recycling cancelling in-flight requests.

The current logging does not provide enough structured context (caller, target URL, duration, correlation ID) to localize the failure. Without instrumentation we cannot distinguish between a legitimate cancellation (user navigated away) and an infrastructure problem (DNS, TLS, dependency outage). The 5x spike is treated as a real signal — investigation must happen before any code change.

This is a **bug investigation + remediation** feature, not a green-field implementation. Surgical changes only.

## Functional Requirements

### FR-1: Locate all SocketException throw and catch sites
Enumerate every code path in the .NET backend (`backend/`) that can produce or catches `SocketException`, directly or via `HttpRequestException` / `OperationCanceledException` wrapping.

**Acceptance criteria:**
- A written inventory exists (in the PR description or a `docs/investigation/socket-exception-2026-05.md` doc) listing each outbound HTTP/socket call site, its target dependency, configured timeout, and whether it currently has retry/circuit-breaker logic.
- Identified all `HttpClient`/`HttpClientFactory` registrations in `Program.cs` / DI extensions.
- Identified any direct `Socket`, `TcpClient`, or `NetworkStream` usage (expected: none, but verify).

### FR-2: Correlate Application Insights data with code paths
Use the App Insights query tooling already available in this project (see `docs/integrations/` and existing App Insights helpers) to extract the failing operation names, target hosts, and HTTP status codes / outcomes for the 24 events.

**Acceptance criteria:**
- A summary table of the 24 events with: timestamp, operation name, target dependency, cloud role instance, exception stack top frame, parent operation ID.
- Identification of whether the spike is concentrated in (a) a single dependency, (b) a single time window suggesting an outage, or (c) an application-recycle / deployment event.
- Comparison with the prior 7 days to confirm the spike is anomalous (not just normal variance amplified by traffic).

### FR-3: Add structured logging around suspected failure points
Where the inventory in FR-1 identifies sites lacking structured logging, add `ILogger`-based structured logs at the point where the exception is caught or propagates. Logs must include enough context to make App Insights queries actionable in the future.

**Acceptance criteria:**
- Failing outbound calls log: target URL (host + path, no secrets), HTTP method, elapsed milliseconds, configured timeout, `CancellationToken.IsCancellationRequested` at the point of failure, retry attempt number (if applicable), correlation/operation ID.
- Log level is `Warning` for cancelled-by-client cases and `Error` for cancelled-by-timeout / unexpected failures (distinguishable).
- No secrets, tokens, or PII appear in log payloads.
- Logging is added via existing logging conventions in the project (no new logging framework introduced).

### FR-4: Distinguish client cancellation from timeout / network failure
The catch sites must classify the failure so dashboards and alerts can ignore benign cancellations.

**Acceptance criteria:**
- When `HttpContext.RequestAborted` (or the inbound caller token) is cancelled and the exception is downstream of that, log at `Warning` with a `reason: "client_aborted"` property and do not re-throw as a 5xx-mapped exception.
- When the cancellation originates from an `HttpClient.Timeout` or per-call `CancellationTokenSource` (i.e., not the inbound request), log at `Error` with `reason: "timeout"` and surface to the caller appropriately.
- When the exception is a true network/DNS/TLS failure with no cancellation, log at `Error` with `reason: "network"`.

### FR-5: Implement appropriate resiliency for confirmed transient failures
For dependencies where FR-2 confirms transient socket failures (not client cancellations), apply a resiliency policy. Prefer the existing approach used in the codebase; do not introduce a new library unless one is already a dependency.

**Acceptance criteria:**
- A retry policy with exponential backoff + jitter is applied to identified transient call sites (e.g., 3 attempts, 200ms–2s backoff).
- Retries are only applied to idempotent operations (GET, or POST/PUT with documented idempotency on the remote side).
- A connection-pool / `PooledConnectionLifetime` review is documented; if a misconfiguration is found, it is fixed (typical fix: set `PooledConnectionLifetime` to a value shorter than upstream load-balancer idle timeout).
- Total worst-case added latency from retries is documented and acceptable (target: < 5s per inbound request).

### FR-6: Validate the fix reduces SocketException rate to baseline
After deploy, the 24h exception count must return to the prior 7-day baseline (≤ ~5/day) within 48 hours, or the team must have a documented next-step.

**Acceptance criteria:**
- A follow-up App Insights query is run 24h and 48h post-deploy.
- The query result is recorded in the PR or the investigation doc.
- If the rate has not dropped, the open question is reopened and a new hypothesis is documented.

## Non-Functional Requirements

### NFR-1: Performance
- Added logging must not measurably impact request latency. Target: < 1 ms p99 overhead per logged failure.
- Retry policies must respect the inbound `HttpContext.RequestAborted` token — no retries after client abort.
- Worst-case retry-induced latency per inbound request: < 5 seconds.

### NFR-2: Security
- No secrets, API keys, bearer tokens, cookies, or PII may be written to logs.
- URLs must be logged without query-string credentials (strip or redact if present).
- No new outbound network endpoints introduced.

### NFR-3: Observability
- All structured log properties must be queryable in App Insights (use property names that survive serialization, avoid nested objects deeper than 1 level).
- Property naming follows existing project conventions (verify in `backend/` before adding).

### NFR-4: Backward compatibility
- No public API contract changes.
- No DTO changes (per project rule: DTOs are classes, never records — irrelevant here since no DTOs change).
- No breaking changes to dependency injection registrations consumed by other modules.

### NFR-5: Reversibility
- Changes must be feature-flag-able or behind configuration where reasonable, so retry policies can be tuned without a redeploy.
- All changes confined to backend; no frontend changes required.

## Data Model
No persistent data model changes. This is a runtime/observability change only.

In-memory / log schema additions (not persisted):
- Structured log fields: `targetHost`, `targetPath`, `httpMethod`, `elapsedMs`, `timeoutMs`, `cancellationRequested`, `attemptNumber`, `reason` (one of `client_aborted` | `timeout` | `network` | `unknown`), `operationId`.

## API / Interface Design
No new HTTP endpoints. No new MediatR commands/queries. No UI changes.

Internal changes only:
- `HttpClient` registrations in `Program.cs` (or relevant DI extension files) may gain `AddPolicyHandler(...)` or equivalent for retry/timeout policies.
- Catch blocks at outbound-call sites updated to classify and log per FR-3 / FR-4.
- Possible addition of a small helper (e.g., `OutboundCallLogger` or extension method on `ILogger`) to standardize the log shape — only if the same logging block is needed in 3+ places (DRY).

## Dependencies
- **Application Insights** — required to validate the spike, identify failing operations, and confirm the fix (FR-2, FR-6).
- **External integrations potentially involved** — Shoptet API, MCP server, ABRA Flexi, Azure Storage / Key Vault / Service Bus, and any third-party HTTP services registered in the backend. Inventory in FR-1 will identify the actual suspect set.
- **Existing resiliency libraries** — if Polly or `Microsoft.Extensions.Http.Resilience` is already referenced, reuse it. Do not add a new package without justification.
- **No external team or vendor dependency expected** unless FR-2 reveals the spike is caused by a confirmed third-party outage, in which case the fix is to harden against it (FR-5), not to wait for the vendor.

## Out of Scope
- Refactoring outbound HTTP plumbing beyond what is needed to fix the spike.
- Migrating to a new HTTP client library or resiliency framework.
- Frontend changes (no React, no E2E test changes).
- Database migrations.
- Adding new App Insights dashboards or alert rules (track separately if needed).
- Investigating non-SocketException error classes, even if they spike concurrently.
- Performance tuning unrelated to the spike (e.g., general latency improvements).
- Changes to deployment pipeline, Docker image, or Azure Web App configuration unless the root cause requires it (in which case a separate change is justified).

## Open Questions
None.

## Status: COMPLETE