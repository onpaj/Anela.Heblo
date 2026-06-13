# Specification: Npgsql Database Connection Resilience & Telemetry

## Summary
Investigate and mitigate a 7-day cluster of ~185 Npgsql/PostgreSQL connection-layer exceptions (~26/day) caused by transient Azure PostgreSQL connection drops. Deliver pool sizing verification, observability for connection pool health, and a hardened Polly-backed EF Core execution strategy so the application self-heals through transient drops instead of surfacing them as user-visible errors.

## Background
Telemetry for the period 2026-06-05 → 2026-06-12 surfaced a sustained cluster of database-layer faults in the `exceptions` table:

- `Npgsql.PostgresException` at `NpgsqlConnector+<ReadMessageLong>` — 120 occurrences (mid-read disconnects).
- `System.Net.Sockets.SocketException` paths through `TaskTimeoutAndCancellation`, `Polly.Outcome.GetResultOrRethrow`, and `NpgsqlConnector.Connect` — 60 occurrences combined.
- `System.TimeoutException` / `TaskCanceledException` / `OperationCanceledException` on `NpgsqlConnector.Connect` and `NpgsqlCommand` — 5 occurrences combined.

The signature is consistent with **transient Azure PostgreSQL connection drops** and possible **connection pool exhaustion under load**. No merged PR in the window addresses Npgsql, pooling, or retry logic. The previously diagnosed EF Core concurrency exceptions on `ArticleRepository.GetFeedbackStatsAsync`/`GetFeedbackPagedAsync` were a separate issue resolved by PR #2915 and are out of scope here.

Rate has declined in the most recent 48 hours (~8 events) versus ~35/day earlier in the window. The work below is justified regardless of whether the issue self-resolves, because the application lacks the telemetry to confirm root cause and the resilience to absorb future drops gracefully.

**Confirmed context (from r1 answers):**
- Production runs against Azure Database for PostgreSQL Flexible Server, treated as **Burstable B2s (2 vCore), `max_connections = 85`** for sizing (FR-1 verifies the actual SKU).
- Production secrets live in Key Vault `kv-heblo-prod` (resource group `rgHeblo`); staging in `kv-heblo-stg`. Connection strings: `ConnectionStrings--Production` / `ConnectionStrings--Staging`.
- Polly v8.4.1 is already referenced from `Anela.Heblo.Application.csproj` and used for outbound HTTP adapters (Anthropic, OpenAI, MetaAds, Flexi, Comgate, Smartsupp, SerpApi) plus `CatalogResilienceService` / `DownloadResilienceService`. **No Polly pipeline currently wraps EF Core / Npgsql** — the `Polly.Outcome.GetResultOrRethrow` frames in telemetry come from adapter HTTP retries that share `SocketException` with the DB layer via TCP infrastructure.
- `PersistenceModule.AddPersistenceServices` (`backend/src/Anela.Heblo.Persistence/PersistenceModule.cs:21`) wires `UseNpgsql(dataSource!)` with no retry strategy. The DB layer is genuinely unprotected today.
- Repository writes go exclusively through `SaveChangesAsync` (a grep for `BeginTransaction` / `UseTransaction` returns no hits in `backend/src`), so each write is an implicit single-statement transaction — safe to retry at the DB layer.

## Functional Requirements

### FR-1: Audit current Npgsql connection string and pool configuration
Document the Npgsql connection string parameters currently in use across environments (Development, Staging, Production) and compare them to recommended values for Azure PostgreSQL Flexible Server. Verify the actual production SKU and `max_connections`.

**Acceptance criteria:**
- A short audit note (in `docs/integrations/` or appended to `docs/architecture/infrastructure.md`) lists current values per environment for: `Max Pool Size`, `Min Pool Size`, `Connection Idle Lifetime`, `Connection Pruning Interval`, `Timeout`, `Command Timeout`, `Keepalive`, `Tcp Keepalive`, `Tcp Keepalive Time`, `Tcp Keepalive Interval`, and `Include Error Detail`.
- The audit captures `Database:MaxPoolSize` (currently 15 in `backend/src/Anela.Heblo.API/appsettings.Production.json:70`, 10 in Staging) and Hangfire's `ConnectionLimit` (currently 5, line 76) so total connection budget is visible in one place.
- The audit verifies production SKU via `az postgres flexible-server show -g rgHeblo -n <server>` and records the observed `max_connections`. If the SKU is not Burstable B2s, the audit updates the FR-2 target accordingly.
- The note flags any parameter that deviates from Azure-recommended values with a recommendation.
- The audit covers any additional `NpgsqlDataSourceBuilder` configuration in code.

### FR-2: Apply hardened connection string defaults
Update Staging and Production connection strings (via `kv-heblo-stg` and `kv-heblo-prod`) and `appsettings.*.json` values to use settings appropriate for Azure PostgreSQL behind a managed network path.

**Acceptance criteria:**
- `Keepalive = 30` (seconds) and `Tcp Keepalive = true` are set, or equivalent values justified in the audit note.
- Production `Database:MaxPoolSize` is set to **20** (up from 15). Hangfire's `ConnectionLimit` stays at 5. The combined ceiling (EF + Hangfire + Analytics DbContext + admin headroom) must stay under the observed `max_connections` minus ~60 connections of headroom.
- Staging `Database:MaxPoolSize` stays at **10** unless the audit recommends otherwise.
- `Connection Idle Lifetime` ≤ Azure idle TCP timeout (default 4 minutes for Azure load balancer) to prevent the pool from holding sockets that the network has already torn down.
- Connection-string changes are made via `az keyvault secret set --vault-name kv-heblo-prod --name "ConnectionStrings--Production" --value "..."` (and the `kv-heblo-stg` equivalent) — never via Azure Portal App Settings.
- Final values are documented in `docs/architecture/environments.md`.

### FR-3: Connection-pool and connection-lifecycle telemetry
Expose Npgsql connection pool metrics and key lifecycle events so the next incident is diagnosable from telemetry alone, without ad-hoc Azure portal queries.

**Acceptance criteria:**
- Npgsql's built-in `EventSource` / `Meter` counters (`npgsql.connections.total`, `npgsql.connections.idle`, `npgsql.connections.busy`, `npgsql.command.duration`, `npgsql.command.failed`, `npgsql.bytes_written`, `npgsql.bytes_read`) are emitted to Application Insights via the existing telemetry sink.
- A custom counter or log records each connection pool exhaustion wait > 1 second with the operation name (where determinable).
- Connection-open failures and mid-read disconnects are logged at `Warning` with structured properties: `exception.type`, `npgsql.host`, `npgsql.database`, `pool.busy`, `pool.idle`. (The existing `PostgresExceptionLoggingInterceptor` at `backend/src/Anela.Heblo.Persistence/Infrastructure/PostgresExceptionLoggingInterceptor.cs:50` already exposes `SqlState`; extend rather than duplicate.)
- A KQL snippet or App Insights workbook reference is committed to `docs/integrations/` so the on-call developer can verify pool health in one click.

### FR-4: Polly-backed EF Core execution strategy for transient faults
Wrap EF Core query execution in a custom `IExecutionStrategy` that delegates to a Polly v8 `ResiliencePipeline` so a single dropped connection does not surface as a 500 to the user.

**Acceptance criteria:**
- A reusable Polly v8 `ResiliencePipeline` is registered as a singleton via `IDbResiliencePipelineProvider`.
- A custom `PollyExecutionStrategy : IExecutionStrategy` delegates `ExecuteAsync` to that pipeline. It is wired via `optionsBuilder.UseNpgsql(dataSource, npgsql => npgsql.ExecutionStrategy(deps => new PollyExecutionStrategy(deps, pipelineProvider)))` in `PersistenceModule.AddPersistenceServices`.
- `EnableRetryOnFailure` is **not** called and no outer Polly pipeline is added around repository calls — there is exactly one retry layer.
- The retry predicate matches **only** transient faults:
  - `Npgsql.PostgresException` with transient `SqlState` codes: `57P01`, `57P02`, `57P03`, and any `08*` (connection_exception) family code.
  - `System.Net.Sockets.SocketException`.
  - `System.TimeoutException`.
  - `System.IO.IOException`.
- The retry predicate **excludes** non-transient logical conflicts: `DbUpdateConcurrencyException`, and any `DbUpdateException` whose inner `PostgresException.SqlState` is in `{23505, 23503, 23502}` (unique_violation, foreign_key_violation, not_null_violation). These rethrow without retry.
- Retry policy: exponential backoff with jitter, max 3 attempts, total time budget ≤ 10 seconds. Final failure rethrows the original exception.
- Each retry is logged at `Warning` with `attempt`, `delay`, and `exception.type`. Final failure is logged at `Error`.
- Telemetry records counters for retry-attempt, retry-success, and retry-failure outcomes so we can quantify how many user-visible errors the policy absorbs.
- No code-level write-transaction carve-out is required (EF Core's `SaveChangesAsync` implicit transactions are inherently retry-safe; no `BeginTransaction` usage exists in the codebase). The execution-strategy contract's prohibition on user-managed transactions is therefore non-breaking.

### FR-5: Alerting on database connection health
Add Azure Monitor alerts so the next regression is caught proactively rather than discovered via weekly telemetry review.

**Acceptance criteria:**
- An alert rule fires when `Npgsql.PostgresException` + `SocketException` (originating in Npgsql stack frames) exceed **10 events / hour for 2 consecutive hours**.
- A second alert fires when connection pool exhaustion waits exceed **5 / 5 minutes**.
- Both alerts route to Azure Monitor Action Group **`ag-heblo-prod-default`** in resource group `rgHeblo`, with a single email receiver: `ondra@anela.cz`.
- If `ag-heblo-prod-default` does not already exist, it is created via `az monitor action-group create` as part of this work and recorded in `docs/architecture/infrastructure.md`.
- Alert configuration lives in infrastructure-as-code or is fully documented in `docs/architecture/infrastructure.md` (Bicep/Terraform if present, otherwise an `az monitor metrics alert create` runbook block).

## Non-Functional Requirements

### NFR-1: Performance
- The execution strategy must add ≤ 1 ms p99 overhead on the success path (no-retry case).
- Telemetry emission must not block the database call path; counters use the in-process `Meter` API and are flushed asynchronously.
- The retry policy's total time budget (≤ 10 s) must respect upstream request timeouts; document the relationship to the API's overall request timeout.

### NFR-2: Security
- Connection string updates flow through Azure Key Vault (`kv-heblo-stg`, `kv-heblo-prod`) only. No secrets in source, App Settings, or logs.
- Structured log properties must not include the connection string, password, or any value derived from secrets.
- `Include Error Detail` remains `false` in Production to avoid leaking schema or parameter values in exception messages; may be `true` in Development.

### NFR-3: Observability
- All new telemetry uses the existing `ILogger` / `Meter` infrastructure — no new sinks or libraries.
- Metrics carry consistent dimension names (`db.system = "postgresql"`, `db.name`, `pool.name`) aligned with OpenTelemetry semantic conventions where they exist.
- Retry/execution-strategy logging shares the property schema used by the existing adapter `ResilienceService` classes so dashboards can correlate.

### NFR-4: Backward compatibility
- Behavioral change must be transparent to callers on the happy path: same exception types are still thrown on final failure.
- Existing tests must continue to pass without modification of business logic.
- The execution strategy's prohibition on user-managed transactions is non-breaking (verified — no `BeginTransaction`/`UseTransaction` callers in `backend/src`).

## Data Model
No persistent data model changes. New ephemeral telemetry only:
- Counters: `npgsql.pool.exhaustion_wait_seconds`, `db.retry.attempts`, `db.retry.success`, `db.retry.failure`.
- Log events: `DbTransientRetry`, `DbTransientRetryExhausted`, `DbPoolExhaustionWait`.

## API / Interface Design
No public API surface changes.

Internal interfaces:
- `IDbResiliencePipelineProvider` (singleton) exposes the configured Polly v8 `ResiliencePipeline` for the EF Core execution strategy.
- `PollyExecutionStrategy : IExecutionStrategy` delegates `ExecuteAsync` to the pipeline; its `RetriesOnFailure` returns `true`.
- `PersistenceModule.AddPersistenceServices` wires `NpgsqlDataSourceBuilder` with the Meter and configures `optionsBuilder.UseNpgsql(dataSource, npgsql => npgsql.ExecutionStrategy(deps => new PollyExecutionStrategy(deps, pipelineProvider)))`.
- New `appsettings.json` section `Database:Resilience` exposes tunable values (`MaxRetryAttempts`, `MaxRetryDelay`, `BaseDelay`) with defaults matching FR-4. Production overrides via Key Vault if needed.

## Dependencies
- **Npgsql** (existing) — version must support EventSource/Meter telemetry; upgrade if current version predates that support. Verify in the audit (FR-1).
- **Polly v8.4.1** (already referenced from `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj:27`). No new package dependency required; the Persistence project will add a `ProjectReference` or direct `PackageReference` to consume Polly.Core.
- **EF Core 8** (existing).
- **Azure Database for PostgreSQL Flexible Server** — Burstable B2s assumed for sizing; audit verifies.
- **Azure Key Vault** `kv-heblo-stg` (staging) and `kv-heblo-prod` (production, resource group `rgHeblo`) for connection-string updates.
- **Azure Monitor Action Group** `ag-heblo-prod-default` (resource group `rgHeblo`) for alerting; created in FR-5 if absent.
- **Application Insights** for telemetry sink (existing).

## Out of Scope
- Migrating off Azure PostgreSQL or changing the database SKU.
- Refactoring `ArticleRepository.GetFeedbackStatsAsync` / `GetFeedbackPagedAsync` — already addressed by PR #2915.
- Read-replica or sharding work.
- Application-level circuit breaker for the database (could be a follow-up if alerts show sustained outages).
- Adding distributed tracing spans for database calls beyond what Npgsql emits natively.
- Frontend changes — this is a backend resilience and observability change only.
- Load testing to reproduce the failure synthetically — this work targets defensive posture, not reproduction.
- Teams/Slack/PagerDuty alert integrations — email-only Action Group for this iteration; future fan-out is a one-receiver change.
- Extending the new resilience pipeline to non-EF code paths (the existing adapter `ResilienceService` classes already have their own Polly pipelines and stay as-is).

## Open Questions
None.

## Status: COMPLETE