# Specification: Fix Recurring DateTime Kind=Unspecified Crash in FlexiAnalyticsSyncJob (Regression from #3243)

## Summary

The nightly `FlexiAnalyticsSyncJob` (`flexi-analytics-sync`) has thrown `System.ArgumentException: Cannot write DateTime with Kind=Unspecified to PostgreSQL type 'timestamp with time zone'` every night from June 19–24 2026, totalling 116 exceptions. Issue #3243 was closed on June 22 as fixed, but the exceptions continued uninterrupted, indicating the code fix was applied to the repository but not deployed to the production Azure Web App for Containers. This specification covers both confirming and closing the deployment gap and hardening the DateTime handling across all four `IEntitySyncService` implementations to prevent any future recurrence.

## Background

`FlexiAnalyticsSyncJob` is a Hangfire recurring job scheduled at `0 3 * * *` in the `Europe/Prague` timezone (≈ 02:00 UTC in summer CEST), which exactly matches the telemetry burst pattern. It drives `FlexiAnalyticsSyncService`, which fans out to four `IEntitySyncService` implementations: `LedgerSyncService`, `ContactSyncService`, `DepartmentSyncService`, and `AccountingTemplateSyncService`. Each syncs data from FlexiBee ERP into the `flexi_raw` schema in PostgreSQL, using the `AnalyticsDbContext` backed by Npgsql.

Npgsql 6+ enforces that any `DateTime` written to a `timestamp with time zone` (`timestamptz`) column must have `DateTimeKind.Utc`. Writing `Kind=Unspecified` causes a hard `ArgumentException` that Polly's `PollyExecutionStrategy` treats as non-transient (it does not retry), so `attempts=1` appears in the log. Hangfire then retries the whole job with exponential backoff, producing the observed burst of 16 at 02:00 UTC and 2 each at 03:00, 04:00, and 06:00 (11 Hangfire retries × approximately 2 telemetry entries each = 22 per night).

### What #3243 actually changed

Commit `745dc72` made two code changes:

1. `FlexiAnalyticsSyncOptions.GetInitialBackfillDateTime()` — changed `DateTimeStyles.AssumeLocal` to `DateTimeStyles.AssumeUniversal` followed by `.Date.ToUniversalTime()`. This ensures the fallback "no watermark" path passes `Kind=Utc` to `ILedgerClient.GetChangedSinceAsync()`.
2. `GridLayoutRepository.UpsertAsync()` — changed `GetUtcNow().DateTime` to `GetUtcNow().UtcDateTime`. This is a separate, unrelated table (`timestamp` without time zone) and was not the source of the reported crash.

The fix addresses only the **initial backfill path** of `LedgerSyncService`. The watermark-exists path (`state.Watermark.Value.AddHours(-1).UtcDateTime`) was already safe because `DateTimeOffset.UtcDateTime` always returns `Kind=Utc`.

### Why the crash continued after #3243

The most likely explanation is that the Docker image was not rebuilt and redeployed to the production Azure Web App for Containers after commit `745dc72` merged on June 22. Deployment is manual (via `deploy-staging-manual.yml` or equivalent production deploy). Evidence: the exception count did not change on June 22–23, and the slight reduction from 22 to 14 starting June 23 is consistent with a schedule/timezone shift (DST transition or cron reconfiguration), not a partial code fix taking effect.

A secondary possibility is that the crash originates in a **different code path** not covered by the #3243 fix — for example, a `DateTime` field returned from the FlexiBee SDK's `GetChangedSinceAsync` response that is subsequently written to a `timestamptz` column via EF Core with `Kind=Unspecified`. The `LedgerItemFlexiDto.AccountingDate` field is stored as `DateOnly` (safe), and `LastUpdate` is converted with `.ToUniversalTime()` before being assigned to `DateTimeOffset?` (also safe). However this must be verified against the actual Hangfire failure record.

## Functional Requirements

### FR-1: Identify the exact failing job invocation and stack trace

Before writing any code, query the production Hangfire database to get the precise exception details.

**Acceptance criteria:**
- Developer has run `SELECT jobid, statename, expireat, data FROM hangfire.job WHERE statename = 'Failed' ORDER BY createdat DESC LIMIT 20;` against the production PostgreSQL instance and retrieved the failed job record(s) from June 19–24.
- The full stack trace from the Hangfire job `data` JSON column has been read and the exact call site (file, method, line) where `Kind=Unspecified` is written to a `timestamptz` column has been identified.
- The result is documented in a code comment or `memory/gotchas/` entry.

### FR-2: Confirm deployment status of the #3243 fix

Determine whether commit `745dc72` (`fix(telemetry): fix DateTime Kind=Unspecified crash in nightly Hangfire job`) is running in production.

**Acceptance criteria:**
- The production Docker image tag (visible in Azure Portal → Web App → Configuration → Docker image tag) has been checked against the Docker Hub tag corresponding to the merge of `745dc72` into `main`.
- If the image is stale, a new production deployment has been triggered and confirmed to be running the image built from `745dc72` or later.
- The production Hangfire dashboard shows no new `flexi-analytics-sync` failures after the next 02:00 UTC run following the deployment.

### FR-3: Fix all remaining DateTime Kind=Unspecified write paths within the FlexiAnalyticsSync pipeline

Whether or not the issue is purely a deployment gap, audit and harden every `DateTime` value that flows from the FlexiBee SDK into a `timestamptz` column through the four sync services.

**Scope of audit:**

| Service | Entity | `timestamptz` columns | DateTime sources from SDK |
|---|---|---|---|
| `LedgerSyncService` | `ledger_entry` | `last_modified`, `synced_at` | `dto.LastUpdate` (mapped via `.ToUniversalTime()`), `DateTimeOffset.UtcNow` |
| `ContactSyncService` | `contact` | `last_modified`, `synced_at` | `dto.LastUpdate` (mapped via `.ToUniversalTime()`), `DateTimeOffset.UtcNow` |
| `DepartmentSyncService` | `department` | `last_modified`, `synced_at` | `dto.LastUpdate` (mapped via `.ToUniversalTime()`), `DateTimeOffset.UtcNow` |
| `AccountingTemplateSyncService` | `accounting_template` | `last_modified`, `synced_at` | `dto.LastUpdate` (mapped via `!= default ? .ToUniversalTime()`), `DateTimeOffset.UtcNow` |
| `LedgerSyncService` (watermark) | `sync_state` | `watermark`, `last_run_started_at`, `last_run_finished_at` | `DateTimeOffset.UtcNow` (safe), `state.Watermark.Value.AddHours(-1).UtcDateTime` |

The specific pattern `dto.LastUpdate?.ToUniversalTime()` returns `DateTime` with `Kind=Utc` if the source is `Kind=Local` or `Kind=Unspecified`. However, the FlexiBee SDK may return `LastUpdate` with `Kind=Unspecified`, and `DateTime.ToUniversalTime()` on `Kind=Unspecified` treats the value as local time — this is correct behavior if the server TZ is `Europe/Prague`, but semantically fragile. The safe replacement is `DateTime.SpecifyKind(dto.LastUpdate.Value, DateTimeKind.Utc)` if the SDK returns UTC values, or explicit `TimeZoneInfo.ConvertTimeToUtc(dto.LastUpdate.Value, TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague"))` if the SDK returns Prague local time.

**Acceptance criteria:**
- Every `.Map()` method in the four sync services assigns `DateTimeOffset?` or `DateTimeOffset` values to `timestamptz` entity properties using values that are either `DateTimeOffset` types (always safe with Npgsql) or `DateTime` with verified `Kind=Utc`.
- No `DateTime` with `Kind=Unspecified` or `Kind=Local` reaches a Npgsql parameter bound to a `timestamptz` column anywhere in the sync pipeline.
- The fix follows the existing pattern in `UnspecifiedDateTimeConverter.cs` and `DateTimeLocalKindConverter.cs` — FlexiBee returns Prague local time; treat as such and convert to UTC explicitly.

### FR-4: Fix the residual bug in `GetInitialBackfillDateTime()`

The #3243 fix introduced a subtle bug: `DateTime.Date` on a `Kind=Utc` value returns `Kind=Unspecified`. The subsequent `.ToUniversalTime()` call on `Kind=Unspecified` treats the value as the server's local timezone, not UTC, yielding a shifted backfill date in Prague time.

Current (post-#3243) code:
```csharp
DateTime.Parse(InitialBackfillFrom, null, System.Globalization.DateTimeStyles.AssumeUniversal)
        .Date
        .ToUniversalTime();
```

On a server with `TZ=Europe/Prague` (UTC+2 in CEST), `"2020-01-01".AssumeUniversal.Date.ToUniversalTime()` produces `2019-12-31T23:00:00Z`, not `2020-01-01T00:00:00Z`. The Npgsql exception is avoided but the backfill window starts one hour late.

**Acceptance criteria:**
- `GetInitialBackfillDateTime()` returns a `DateTime` with `Kind=Utc` whose date component matches `InitialBackfillFrom` when interpreted as UTC midnight.
- The correct implementation is `DateTime.Parse(InitialBackfillFrom, null, DateTimeStyles.AssumeUniversal).ToUniversalTime()` (drop `.Date`, which destroys `Kind`), or better: return a `DateTimeOffset` directly: `DateTimeOffset.Parse(InitialBackfillFrom, null, DateTimeStyles.AssumeUniversal).UtcDateTime`.
- The existing unit test in `FlexiAnalyticsSyncOptionsTests` is updated or extended to assert both `Kind=Utc` and that the date component equals the configured `InitialBackfillFrom` date as UTC midnight.

### FR-5: Add a regression test that detects Kind=Unspecified before it reaches Npgsql

**Acceptance criteria:**
- A unit test in `LedgerSyncServiceTests` (or a new `LedgerSyncServiceDateTimeTests`) verifies that when `GetChangedSinceAsync` is called via `LedgerSyncService.SyncAsync()`, the `DateTime` argument passed to the client has `Kind=Utc` in both the no-watermark and watermark-exists cases.
- A parallel test exercises the `Map()` static method in `LedgerSyncService` with a `LedgerItemFlexiDto` whose `LastUpdate` has `Kind=Unspecified` and asserts that the resulting `LedgerEntry.LastModified` is a `DateTimeOffset` in UTC.
- Equivalent tests are added or verified for `ContactSyncService`, `DepartmentSyncService`, and `AccountingTemplateSyncService`.

## Non-Functional Requirements

### NFR-1: Performance

No performance impact is expected. All changes are in DateTime construction paths (CPU-only, negligible cost) and do not affect query logic, batching, or network calls.

### NFR-2: Reliability

After this fix, the `flexi-analytics-sync` job must complete without `ArgumentException` on every nightly run. The Hangfire retry pattern (16 + 2 + 2 + 2 = 22 per night) must not recur.

### NFR-3: Observability

No new telemetry instrumentation is required. The existing `PollyExecutionStrategy` log line `DbTransientRetryExhausted` and the Hangfire failure state are sufficient to detect any regression. Monitor Application Insights for `exception:ArgumentException@DateTimeConverterResolver.Get` after deployment.

### NFR-4: Security

No security implications. Changes are confined to internal data mapping logic with no user-facing surface.

## Data Model

No schema changes. All `timestamp with time zone` columns in `flexi_raw.*` remain unchanged. The fix is entirely in the application layer.

Relevant columns (all `timestamptz`):

| Table | Columns |
|---|---|
| `flexi_raw.ledger_entry` | `last_modified`, `synced_at` |
| `flexi_raw.contact` | `last_modified`, `synced_at` |
| `flexi_raw.department` | `last_modified`, `synced_at` |
| `flexi_raw.accounting_template` | `last_modified`, `synced_at` |
| `flexi_raw.sync_state` | `watermark`, `last_run_started_at`, `last_run_finished_at` |

## API / Interface Design

No API or interface changes. `IEntitySyncService`, `ISyncWatermarkRepository`, and `IFlexiAnalyticsSyncService` signatures are unchanged. `FlexiAnalyticsSyncOptions.GetInitialBackfillDateTime()` return type remains `DateTime` (preserving the `ILedgerClient.GetChangedSinceAsync(DateTime since, ...)` call site).

## Dependencies

- **Npgsql 6+**: The breaking constraint. `DateTime` parameters bound to `timestamptz` must have `Kind=Utc`. `DateTimeOffset` parameters are always accepted regardless of offset.
- **Rem.FlexiBeeSDK.Client 0.1.138**: `ILedgerClient.GetChangedSinceAsync(DateTime since, int? limit, int? skip, CancellationToken)` — accepts `DateTime`, so `Kind` must be `Utc` before calling.
- **FlexiBee ERP**: Returns datetime values in Prague local time (`Europe/Prague`). The existing `UnspecifiedDateTimeConverter` and `DateTimeLocalKindConverter` handle this for JSON/AutoMapper paths; the sync services' `Map()` methods must apply the same conversion.
- **Hangfire**: Retries failed jobs with exponential backoff. The exception count pattern (16 + 2 + 2 + 2) is Hangfire behavior and will stop once the root cause is fixed.
- **Azure Web App for Containers / Docker Hub**: The production deployment must be triggered manually after the fix is merged to `main`.

## Out of Scope

- Changes to the `flexi_raw` database schema.
- Changes to the Hangfire retry policy or Polly `PollyExecutionStrategy` (the current behavior of not retrying `ArgumentException` is correct — it is not a transient error).
- Changes to `GridLayoutRepository` (already fixed in #3243; the `GridLayouts.LastModified` column is `timestamp without time zone`, not `timestamptz`, making it lower priority anyway).
- Fixing the `UnspecifiedDateTimeConverter` or `DateTimeLocalKindConverter` — these are not in the crash path.
- Automated deployment pipeline changes (out of scope for this bug fix).
- E2E tests for this path (background jobs are not covered by Playwright E2E per the testing strategy).

## Open Questions

1. **Exact crash site**: The brief identifies `DateTimeConverterResolver.Get` as the Npgsql throw point but does not include the full managed stack trace showing which `Map()` method or which `dto.LastUpdate` call is the immediate cause. Running the Hangfire DB query (`SELECT jobid, statename, expireat, data FROM hangfire.job WHERE statename = 'Failed' ORDER BY createdat DESC LIMIT 20;`) against production must be done before coding FR-3 to confirm whether the crash is in `LedgerSyncService`, one of the other three services, or the `SyncWatermarkRepository`.

2. **Deployment gap confirmation**: Was commit `745dc72` (merged June 22, 07:50 UTC+2) included in a production Docker image build and deployed before the June 22 nightly run at 02:00 UTC? If yes, the root cause is a different code path not patched by #3243. If no, the deployment gap is the sole cause and FR-3/FR-4 are hardening rather than crash fixes.

3. **FlexiBee SDK `LastUpdate` timezone contract**: `LedgerItemFlexiDto.LastUpdate` is `DateTime?`. Does the SDK return it with `Kind=Unspecified` (Prague local), `Kind=Local`, or `Kind=Utc`? The current code calls `.ToUniversalTime()` which behaves differently for each Kind. If `Kind=Unspecified`, the server's `TZ` environment variable determines the conversion. Confirming this against the SDK docs or a debug run will determine whether `DateTime.SpecifyKind(..., DateTimeKind.Utc)` or `TimeZoneInfo.ConvertTimeToUtc(..., pragueZone)` is the correct replacement.

## Status: HAS_QUESTIONS
