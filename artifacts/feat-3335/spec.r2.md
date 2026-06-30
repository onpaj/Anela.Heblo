# Specification: Fix Recurring DateTime Kind=Unspecified Crash in FlexiAnalyticsSyncJob (Regression from #3243)

## Summary

The nightly `FlexiAnalyticsSyncJob` (`flexi-analytics-sync`) has thrown `System.ArgumentException: Cannot write DateTime with Kind=Unspecified to PostgreSQL type 'timestamp with time zone'` every night from June 19–24 2026, totalling 116 exceptions. Issue #3243 was closed on June 22 as fixed, but no production deployment has occurred since that commit merged — the nightly run at 02:00 UTC on June 22 pre-dated the merge, and June 23–24 exceptions confirm the stale image is still running. This specification covers deploying the existing fix, hardening all four `Map()` methods in the sync pipeline to use the correct Prague-local-to-UTC conversion, and fixing a residual `.Date` bug introduced by #3243.

## Background

`FlexiAnalyticsSyncJob` is a Hangfire recurring job scheduled at `0 3 * * *` in the `Europe/Prague` timezone (≈ 02:00 UTC in summer CEST), which exactly matches the telemetry burst pattern. It drives `FlexiAnalyticsSyncService`, which fans out to four `IEntitySyncService` implementations: `LedgerSyncService`, `ContactSyncService`, `DepartmentSyncService`, and `AccountingTemplateSyncService`. Each syncs data from FlexiBee ERP into the `flexi_raw` schema in PostgreSQL, using the `AnalyticsDbContext` backed by Npgsql.

Npgsql 6+ enforces that any `DateTime` written to a `timestamp with time zone` (`timestamptz`) column must have `DateTimeKind.Utc`. Writing `Kind=Unspecified` causes a hard `ArgumentException` that Polly's `PollyExecutionStrategy` treats as non-transient (it does not retry), so `attempts=1` appears in the log. Hangfire then retries the whole job with exponential backoff, producing the observed burst of 16 at 02:00 UTC and 2 each at 03:00, 04:00, and 06:00 (11 Hangfire retries × approximately 2 telemetry entries each = 22 per night).

### What #3243 actually changed

Commit `745dc72` made two code changes:

1. `FlexiAnalyticsSyncOptions.GetInitialBackfillDateTime()` — changed `DateTimeStyles.AssumeLocal` to `DateTimeStyles.AssumeUniversal` followed by `.Date.ToUniversalTime()`. This ensures the fallback "no watermark" path passes `Kind=Utc` to `ILedgerClient.GetChangedSinceAsync()`. However, this introduced a subtle secondary bug (see FR-4).
2. `GridLayoutRepository.UpsertAsync()` — changed `GetUtcNow().DateTime` to `GetUtcNow().UtcDateTime`. This is a separate, unrelated table (`timestamp` without time zone) and was not the source of the reported crash.

The fix addresses only the **initial backfill path** of `LedgerSyncService`. The watermark-exists path (`state.Watermark.Value.AddHours(-1).UtcDateTime`) was already safe because `DateTimeOffset.UtcDateTime` always returns `Kind=Utc`.

### Why the crash continued after #3243

No deployment occurred after commit `745dc72` merged on June 22 at 05:50 UTC. The June 22 nightly run fired at 02:00 UTC — nearly four hours before the merge — so the fixed image was never built in time for that run. The `ci-main-branch.yml` pipeline includes a manual approval gate for production deployments, meaning no automatic deploy followed the merge. June 23 and June 24 exceptions confirm the image had not been updated in the 48 hours after merge. FR-3 and FR-4 are defensive hardening; the primary fix is triggering the production deployment.

### Most probable crash site

The crash is almost certainly in `LedgerSyncService.Map()` at the line `LastModified = dto.LastUpdate?.ToUniversalTime()`. The FlexiBee SDK returns `LastUpdate` with `Kind=Unspecified` representing Prague local time. `DateTime.ToUniversalTime()` on `Kind=Unspecified` happens to convert correctly when the container's `TZ` is `Europe/Prague`, but this is semantically fragile — it relies on an implicit ambient timezone assumption rather than an explicit conversion. The authoritative crash site must be confirmed via the Hangfire DB query (FR-1) before coding FR-3, but all four `Map()` methods and `GetInitialBackfillDateTime()` must be hardened regardless.

### Correct DateTime conversion pattern

The FlexiBee SDK returns `DateTime` values with `Kind=Unspecified` representing Prague local time. The correct fix is explicit timezone conversion:

```csharp
TimeZoneInfo.ConvertTimeToUtc(dto.LastUpdate.Value, TimeZoneInfo.Local)
```

This matches the established pattern in `UnspecifiedDateTimeConverter.cs` and `DateTimeLocalKindConverter.cs`. The current `.ToUniversalTime()` call works on Prague-TZ containers but is fragile; replacing it with explicit `ConvertTimeToUtc` eliminates the ambient-timezone dependency.

## Functional Requirements

### FR-1: Identify the exact failing job invocation and stack trace

Before writing any code changes for FR-3, query the production Hangfire database to get the precise exception details and confirm `LedgerSyncService.Map()` as the crash site (or identify a different site).

**Acceptance criteria:**
- Developer has run `SELECT jobid, statename, expireat, data FROM hangfire.job WHERE statename = 'Failed' ORDER BY createdat DESC LIMIT 20;` against the production PostgreSQL instance and retrieved the failed job record(s) from June 19–24.
- The full stack trace from the Hangfire job `data` JSON column has been read and the exact call site (file, method, line) where `Kind=Unspecified` is written to a `timestamptz` column has been identified. Expected finding: `LedgerSyncService.Map()` at `LastModified = dto.LastUpdate?.ToUniversalTime()`.
- The result is documented in a code comment or `memory/gotchas/` entry.

### FR-2: Deploy commit `745dc72` to production

Trigger a production deployment so the image running in the Azure Web App for Containers includes commit `745dc72` or any later commit on `main`.

**Acceptance criteria:**
- The manual approval gate in `ci-main-branch.yml` has been approved and the production deployment pipeline has completed successfully.
- The production Docker image tag (visible in Azure Portal → Web App → Configuration → Docker image tag) corresponds to the image built from `745dc72` or a later `main` commit.
- The production Hangfire dashboard shows no new `flexi-analytics-sync` failures after the next 02:00 UTC run following the deployment.

### FR-3: Fix all remaining DateTime Kind=Unspecified write paths within the FlexiAnalyticsSync pipeline

Audit and harden every `DateTime` value that flows from the FlexiBee SDK into a `timestamptz` column through the four sync services. Apply the explicit Prague-to-UTC conversion pattern to every `dto.LastUpdate` mapping site, eliminating reliance on the container's ambient `TZ` setting.

**Scope of audit:**

| Service | Entity | `timestamptz` columns | DateTime sources from SDK |
|---|---|---|---|
| `LedgerSyncService` | `ledger_entry` | `last_modified`, `synced_at` | `dto.LastUpdate` (currently mapped via `?.ToUniversalTime()`), `DateTimeOffset.UtcNow` |
| `ContactSyncService` | `contact` | `last_modified`, `synced_at` | `dto.LastUpdate` (currently mapped via `?.ToUniversalTime()`), `DateTimeOffset.UtcNow` |
| `DepartmentSyncService` | `department` | `last_modified`, `synced_at` | `dto.LastUpdate` (currently mapped via `?.ToUniversalTime()`), `DateTimeOffset.UtcNow` |
| `AccountingTemplateSyncService` | `accounting_template` | `last_modified`, `synced_at` | `dto.LastUpdate` (currently mapped via `!= default ? .ToUniversalTime()`), `DateTimeOffset.UtcNow` |
| `LedgerSyncService` (watermark) | `sync_state` | `watermark`, `last_run_started_at`, `last_run_finished_at` | `DateTimeOffset.UtcNow` (safe), `state.Watermark.Value.AddHours(-1).UtcDateTime` (safe) |

**Required replacement pattern** for all `dto.LastUpdate` usages:

```csharp
// Before (fragile — relies on container TZ=Europe/Prague)
LastModified = dto.LastUpdate?.ToUniversalTime()

// After (explicit — matches UnspecifiedDateTimeConverter.cs and DateTimeLocalKindConverter.cs)
LastModified = dto.LastUpdate.HasValue
    ? TimeZoneInfo.ConvertTimeToUtc(dto.LastUpdate.Value, TimeZoneInfo.Local)
    : (DateTime?)null
```

**Acceptance criteria:**
- Every `.Map()` method in the four sync services assigns `DateTimeOffset?` or `DateTimeOffset` values to `timestamptz` entity properties using values that are either `DateTimeOffset` types (always safe with Npgsql) or `DateTime` with verified `Kind=Utc`.
- No `DateTime` with `Kind=Unspecified` or `Kind=Local` reaches a Npgsql parameter bound to a `timestamptz` column anywhere in the sync pipeline.
- The replacement uses `TimeZoneInfo.ConvertTimeToUtc(value, TimeZoneInfo.Local)` — the same pattern as `UnspecifiedDateTimeConverter.cs` and `DateTimeLocalKindConverter.cs` — explicitly treating the SDK value as Prague local time.

### FR-4: Fix the residual bug in `GetInitialBackfillDateTime()`

The #3243 fix introduced a subtle bug: `DateTime.Date` on a `Kind=Utc` value returns `Kind=Unspecified`. The subsequent `.ToUniversalTime()` call on `Kind=Unspecified` treats the value as the server's local timezone, not UTC, yielding a shifted backfill date in Prague time.

Current (post-#3243) code:
```csharp
DateTime.Parse(InitialBackfillFrom, null, System.Globalization.DateTimeStyles.AssumeUniversal)
        .Date
        .ToUniversalTime();
```

On a server with `TZ=Europe/Prague` (UTC+2 in CEST), `"2020-01-01".AssumeUniversal.Date.ToUniversalTime()` produces `2019-12-31T23:00:00Z`, not `2020-01-01T00:00:00Z`. The Npgsql exception is avoided but the backfill window starts one hour late (two hours in CET).

**Acceptance criteria:**
- `GetInitialBackfillDateTime()` returns a `DateTime` with `Kind=Utc` whose date component matches `InitialBackfillFrom` when interpreted as UTC midnight.
- The correct implementation drops `.Date` (which destroys `Kind`) and parses directly to UTC:
  ```csharp
  // Option A — minimal change
  DateTime.Parse(InitialBackfillFrom, null, DateTimeStyles.AssumeUniversal).ToUniversalTime()

  // Option B — preferred, explicitly returns Kind=Utc via DateTimeOffset
  DateTimeOffset.Parse(InitialBackfillFrom, null, DateTimeStyles.AssumeUniversal).UtcDateTime
  ```
- The existing unit test in `FlexiAnalyticsSyncOptionsTests` is updated or extended to assert both `Kind=Utc` and that the date component equals the configured `InitialBackfillFrom` date as UTC midnight (i.e., `2020-01-01T00:00:00Z`, not `2019-12-31T23:00:00Z`).

### FR-5: Add regression tests that detect Kind=Unspecified before it reaches Npgsql

**Acceptance criteria:**
- A unit test in `LedgerSyncServiceTests` (or a new `LedgerSyncServiceDateTimeTests`) verifies that when `GetChangedSinceAsync` is called via `LedgerSyncService.SyncAsync()`, the `DateTime` argument passed to the client has `Kind=Utc` in both the no-watermark and watermark-exists cases.
- A parallel test exercises the `Map()` static method in `LedgerSyncService` with a `LedgerItemFlexiDto` whose `LastUpdate` has `Kind=Unspecified` and asserts that the resulting `LedgerEntry.LastModified` is a `DateTime` (or `DateTimeOffset`) in UTC — specifically that the value matches `TimeZoneInfo.ConvertTimeToUtc(input, TimeZoneInfo.Local)`.
- Equivalent tests are added or verified for `ContactSyncService`, `DepartmentSyncService`, and `AccountingTemplateSyncService` covering the same `Kind=Unspecified` input scenario.

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
- **Rem.FlexiBeeSDK.Client 0.1.138**: `ILedgerClient.GetChangedSinceAsync(DateTime since, int? limit, int? skip, CancellationToken)` — accepts `DateTime`, so `Kind` must be `Utc` before calling. SDK returns `LastUpdate` with `Kind=Unspecified` representing Prague local time — this is the root SDK contract to code against.
- **FlexiBee ERP**: Returns datetime values in Prague local time (`Europe/Prague`). The existing `UnspecifiedDateTimeConverter` and `DateTimeLocalKindConverter` handle this for JSON/AutoMapper paths; the sync services' `Map()` methods must apply the same explicit conversion via `TimeZoneInfo.ConvertTimeToUtc`.
- **Hangfire**: Retries failed jobs with exponential backoff. The exception count pattern (16 + 2 + 2 + 2) is Hangfire behavior and will stop once the root cause is fixed.
- **Azure Web App for Containers / Docker Hub**: The production deployment must be triggered manually via the approval gate in `ci-main-branch.yml` after the fix is merged to `main`.

## Out of Scope

- Changes to the `flexi_raw` database schema.
- Changes to the Hangfire retry policy or Polly `PollyExecutionStrategy` (the current behavior of not retrying `ArgumentException` is correct — it is not a transient error).
- Changes to `GridLayoutRepository` (already fixed in #3243; the `GridLayouts.LastModified` column is `timestamp without time zone`, not `timestamptz`, making it lower priority anyway).
- Fixing the `UnspecifiedDateTimeConverter` or `DateTimeLocalKindConverter` — these are not in the crash path.
- Automated deployment pipeline changes to remove the manual approval gate (out of scope for this bug fix).
- E2E tests for this path (background jobs are not covered by Playwright E2E per the testing strategy).

## Open Questions

None.

## Status: COMPLETE
