# Specification: Npgsql Database Connection Resilience & Telemetry

## Summary
Investigate and mitigate a 7-day cluster of ~185 Npgsql/PostgreSQL connection-layer exceptions (~26/day) caused by transient Azure PostgreSQL connection drops. Deliver pool sizing verification, observability for connection pool health, and a hardened Polly-based resilience layer around EF Core query execution so the application self-heals through transient drops instead of surfacing them as user-visible errors.

## Background
Telemetry for the period 2026-06-05 → 2026-06-12 surfaced a sustained cluster of database-layer faults in the `exceptions` table:

- `Npgsql.PostgresException` at `NpgsqlConnector+<ReadMessageLong>` — 120 occurrences (mid-read disconnects).
- `System.Net.Sockets.SocketException` paths through `TaskTimeoutAndCancellation`, `Polly.Outcome.GetResultOrRethrow`, and `NpgsqlConnector.Connect` — 60 occurrences combined.
- `System.TimeoutException` / `TaskCanceledException` / `OperationCanceledException` on `NpgsqlConnector.Connect` and `NpgsqlCommand` — 5 occurrences combined.

The signature is consistent with **transient Azure PostgreSQL connection drops** and possible **connection pool exhaustion under load**. No merged PR in the window addresses Npgsql, pooling, or retry logic. The previously diagnosed EF Core concurrency exceptions on `ArticleRepository.GetFeedbackStatsAsync`/`GetFeedbackPagedAsync` were a separate issue resolved by PR #2915 and are out of scope here.

Rate has declined in the most recent 48 hours (~8 events) versus ~35/day earlier in the window. The work below is justified regardless of whether the issue self-resolves, because the application lacks the telemetry to confirm root cause and the resilience to absorb future drops gracefully.

**Assumption:** The app runs on a single Azure Web App for Containers instance against an Azure Database for PostgreSQL Flexible Server. The Npgsql connection string today uses default `Max Pool Size = 100` and no explicit `Keepalive` or `Tcp Keepalive` settings. (To be verified — see Open Questions.)

## Functional Requirements

### FR-1: Audit current Npgsql connection string and pool configuration
Document the Npgsql connection string parameters currently in use across environments (Development, Staging, Production) and compare them to recommended values for Azure PostgreSQL Flexible Server.

**Acceptance criteria:**
- A short audit note (in `docs/integrations/` or appended to the relevant infra doc) lists current values for `Max Pool Size`, `Min Pool Size`, `Connection Idle Lifetime`, `Connection Pruning Interval`, `Timeout`, `Command Timeout`, `Keepalive`, `Tcp Keepalive`, `Tcp Keepalive Time`, `Tcp Keepalive Interval`, and `Include Error Detail` for each environment.
- The note flags any parameter that deviates from Azure-recommended values with a recommendation.
- The audit covers any additional `NpgsqlDataSourceBuilder` configuration in code.

### FR-2: Apply hardened connection string defaults
Update Staging and Production connection strings (via Azure Key Vault `kv-heblo-stg`, and the production vault if applicable) to use values appropriate for Azure PostgreSQL behind a managed network path.

**Acceptance criteria:**
- `Keepalive = 30` (seconds) and `Tcp Keepalive = true` are set, or equivalent values justified in the audit note.
- `Max Pool Size` is set explicitly (not defaulted) at a value that fits within the Azure DB SKU's `max_connections` limit minus headroom for other consumers.
- `Connection Idle Lifetime` ≤ Azure idle TCP timeout (default 4 minutes for Azure load balancer) to prevent the pool from holding sockets that the network has already torn down.
- Connection-string changes are made via `az keyvault secret set` against `kv-heblo-stg` (and production equivalent) — never via Azure Portal App Settings.
- Values are documented in `docs/architecture/environments.md`.

### FR-3: Connection-pool and connection-lifecycle telemetry
Expose Npgsql connection pool metrics and key lifecycle events so the next incident is diagnosable from telemetry alone, without ad-hoc Azure portal queries.

**Acceptance criteria:**
- Npgsql's built-in `EventSource` / `Meter` counters (`npgsql.connections.total`, `npgsql.connections.idle`, `npgsql.connections.busy`, `npgsql.command.duration`, `npgsql.command.failed`, `npgsql.bytes_written`, `npgsql.bytes_read`) are emitted to the application's existing telemetry sink (Application Insights).
- A custom counter or log records each connection pool exhaustion wait > 1 second with the operation name (where determinable).
- Connection-open failures and mid-read disconnects are logged at `Warning` with structured properties: `exception.type`, `npgsql.host`, `npgsql.database`, `pool.busy`, `pool.idle`.
- Dashboard/queries are documented (kql snippet or App Insights workbook reference) in `docs/integrations/` so the on-call developer can verify pool health in one click.

### FR-4: Polly-based transient-fault resilience around EF Core
Wrap EF Core query execution in a Polly retry policy targeting only Npgsql/socket transient faults so that a single dropped connection does not surface as a 500 to the user.

**Acceptance criteria:**
- A reusable Polly v8 `ResiliencePipeline` (or equivalent) is registered for the database connection and applied via Npgsql's `EnableRetryOnFailure` or an EF Core execution strategy.
- The strategy retries only on the documented transient exceptions: `Npgsql.PostgresException` with transient `SqlState` codes (e.g. `57P01`, `57P02`, `57P03`, `08*`), `System.Net.Sockets.SocketException`, `System.TimeoutException`, and `System.IO.IOException`.
- Retry policy: exponential backoff with jitter, max 3 attempts, total time budget ≤ 10 seconds. Final failure rethrows the original exception.
- Each retry is logged at `Warning` with `attempt`, `delay`, and `exception.type`. Final failure is logged at `Error`.
- Retries are **not** applied to write transactions where idempotency cannot be guaranteed; document the carve-out.
- Telemetry records a counter for retry-attempt and retry-success outcomes, so we can quantify how many user-visible errors the policy absorbs.

### FR-5: Alerting on database connection health
Add an alert (Azure Monitor or equivalent) that fires when the rate of Npgsql exceptions exceeds a baseline threshold, so the next regression is caught proactively rather than discovered via weekly telemetry review.

**Acceptance criteria:**
- An alert rule fires when `Npgsql.PostgresException` + `SocketException` (originating in Npgsql stack frames) exceed 10 events / hour for 2 consecutive hours.
- A second alert fires when connection pool exhaustion waits exceed 5 / 5 minutes.
- Alerts route to the same notification channel used by existing application alerts (TBD — see Open Questions).
- Alert configuration lives in infrastructure-as-code or is documented in `docs/architecture/infrastructure.md`.

## Non-Functional Requirements

### NFR-1: Performance
- The resilience pipeline must add ≤ 1 ms p99 overhead on the success path (no-retry case).
- Telemetry emission must not block the database call path; counters use the in-process `Meter` API and are flushed asynchronously.
- The retry policy's total time budget (≤ 10 s) must respect upstream request timeouts; document the relationship to the API's overall request timeout.

### NFR-2: Security
- Connection string updates flow through Azure Key Vault only. No secrets in source, App Settings, or logs.
- Structured log properties must not include the connection string, password, or any value derived from secrets.
- `Include Error Detail` remains `false` in Production to avoid leaking schema or parameter values in exception messages; may be `true` in Development.

### NFR-3: Observability
- All new telemetry uses the existing `ILogger` / `Meter` infrastructure — no new sinks or libraries.
- Metrics carry consistent dimension names (`db.system = "postgresql"`, `db.name`, `pool.name`) aligned with OpenTelemetry semantic conventions where they exist.

### NFR-4: Backward compatibility
- Behavioral change must be transparent to callers on the happy path: same exception types are still thrown on final failure.
- Existing tests must continue to pass without modification of business logic.

## Data Model
No persistent data model changes. New ephemeral telemetry only:
- Counters: `npgsql.pool.exhaustion_wait_seconds`, `db.retry.attempts`, `db.retry.success`, `db.retry.failure`.
- Log events: `DbTransientRetry`, `DbTransientRetryExhausted`, `DbPoolExhaustionWait`.

## API / Interface Design
No public API surface changes.

Internal interfaces:
- A `IDbResiliencePipelineProvider` (or analogous) exposes the configured Polly pipeline for the EF Core `DbContext` registration.
- `Program.cs` / composition root wires `NpgsqlDataSourceBuilder` with the Meter and configures `optionsBuilder.UseNpgsql(..., npgsql => npgsql.EnableRetryOnFailure(...))` or uses Polly directly via an execution strategy.
- A new `appsettings.json` section `Database:Resilience` exposes tunable values (`MaxRetryAttempts`, `MaxRetryDelay`, `BaseDelay`) with defaults matching FR-4. Production overrides via Key Vault if needed.

## Dependencies
- **Npgsql** (existing) — version must support EventSource/Meter telemetry; upgrade if current version predates that support. Verify in the audit (FR-1).
- **Polly v8** (`Polly.Core`, `Microsoft.Extensions.Http.Resilience` if relevant) — add if not already present. Project may already pull in Polly transitively; confirm in audit.
- **EF Core 8** (existing).
- **Azure Database for PostgreSQL Flexible Server** — `max_connections` and SKU tier inform `Max Pool Size`.
- **Azure Key Vault** `kv-heblo-stg` (and production vault) for connection-string updates.
- **Application Insights** for telemetry sink (existing).

## Out of Scope
- Migrating off Azure PostgreSQL or changing the database SKU.
- Refactoring `ArticleRepository.GetFeedbackStatsAsync` / `GetFeedbackPagedAsync` — already addressed by PR #2915.
- Read-replica or sharding work.
- Application-level circuit breaker for the database (could be a follow-up if alerts show sustained outages).
- Adding distributed tracing spans for database calls beyond what Npgsql emits natively.
- Frontend changes — this is a backend resilience and observability change only.
- Load testing to reproduce the failure synthetically — this work targets defensive posture, not reproduction.

## Open Questions
1. **Production vault name and access.** `kv-heblo-stg` is documented for staging. What is the production Key Vault name, and does the same `ConnectionStrings--Production` naming convention apply?
2. **Azure PostgreSQL SKU and `max_connections`.** What tier is the production database, and what is its `max_connections` ceiling? This determines the correct `Max Pool Size` value for FR-2.
3. **Alert notification channel.** Where should the alerts in FR-5 route — email, Slack, Teams, PagerDuty, or an existing Azure Action Group? CLAUDE.md notes "solo developer + AI-assisted PR review," so likely a single email/notification channel, but confirmation needed.
4. **Polly version already in use.** The brief references "Polly retries" in the stack trace (`Polly.Outcome.GetResultOrRethrow`), implying Polly is already wired somewhere. Where, and what does it currently cover? This may change FR-4 from "add resilience" to "extend / fix existing resilience."
5. **Write transaction policy.** Are there write operations that are not safely idempotent under retry (e.g., non-transactional multi-statement writes)? If yes, the carve-out in FR-4 needs an explicit list.
6. **EF Core execution strategy vs. Polly outer wrapper.** `EnableRetryOnFailure` and an outer Polly pipeline can conflict (double-retry, transaction rollback issues). Confirm which layer is canonical for this codebase.

## Status: HAS_QUESTIONS