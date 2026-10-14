---
process: job-recurring-job-registration
kind: job
module: background-jobs
summary: How every recurring job gets its database row, its Hangfire schedule, its on/off switch, an admin cron override and a manual "Run now", and how failed runs are reported.
owns:
  - backend/src/Anela.Heblo.Application/Features/BackgroundJobs/**
  - backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/**
  - backend/src/Anela.Heblo.API/Infrastructure/Hangfire/**
  - backend/src/Anela.Heblo.API/Controllers/RecurringJobsController.cs
  - backend/src/Anela.Heblo.Persistence/BackgroundJobs/**
  - backend/src/Anela.Heblo.Xcc/HangfireOptions.cs
verified_at: "5e993f9e2"
related: [sync-flexi-analytics]
---

# Recurring job registration, switches and manual runs

## Purpose
Explains why a scheduled job runs (or doesn't) at a given time. For Anela staff the questions are
"why did the 4 a.m. import not run?", "I switched the job off — why does Hangfire still show it?",
"can I run the import now?". Administrators act on the **Recurring Jobs** page (`/recurring-jobs`);
developers add a job by writing one class.

## Trigger
- **Application start** (every deploy / restart): seeding, then Hangfire registration.
- **Admin actions** on `/recurring-jobs`: enable/disable, change cron, Run now.
- **Hangfire's own scheduler** then fires each registered job on its cron (Europe/Prague unless the
  job says otherwise).

## Data flow
Code (`IRecurringJob` classes) → `public."RecurringJobConfigurations"` → Hangfire recurring
definitions in schema `hangfire_heblo` → Hangfire worker → the job's `ExecuteAsync`.

1. **Discovery into DI** (`AddRecurringJobs`): every non-abstract class implementing
   `IRecurringJob` in the `Anela.Heblo.Application` assembly is registered (scoped) as
   `IRecurringJob` and as itself. Adapter jobs (Flexi, GA4, Shoptet orders, Meta/Google Ads) are
   registered by their adapter's `ServiceCollectionExtensions`, some only when configured.
2. **Seeding** (`Program.cs` → `SeedRecurringJobConfigurationsAsync` → `RecurringJobSeeder`), after
   EF migrations and before the host starts:
   - resolves all `IRecurringJob` instances (constructs each job and its dependencies);
   - job id missing in `RecurringJobConfigurations` → inserts a row from metadata (`CronExpression`,
     `DefaultIsEnabled`, `TimeZoneId`, `LastModifiedBy = "System"`);
   - row exists → updates only `DisplayName`, `Description`, `TimeZoneId` when they differ from code;
     **`CronExpression` and `IsEnabled` are never touched**;
   - any exception here **stops the application from starting** (rethrown on purpose).
3. **Registration with Hangfire** (`RecurringJobDiscoveryService`, hosted service at startup):
   - if `Hangfire:SchedulerEnabled` is false → logs and registers nothing;
   - otherwise, for each job: cron = the DB row's `CronExpression` (falls back to metadata if no
     row), time zone = metadata `TimeZoneId` → `HangfireJobRegistrationHelper.RegisterOrUpdate` →
     `RecurringJob.AddOrUpdate<TJob>(jobId, job => job.ExecuteAsync(default), cron, {TimeZone})`.
     A failure for one job is logged and the others continue. `IsEnabled` is **not** consulted.
4. **Firing**: Hangfire's scheduler enqueues the job at each cron occurrence into queue `default`;
   the single worker (`Hangfire:WorkerCount` = 1) picks it up (queue poll every 15 s) and calls
   `ExecuteAsync`. By convention the job's first statement is
   `IRecurringJobStatusChecker.IsJobEnabledAsync(jobId)`; when it returns false the job logs and
   returns — Hangfire records the run as **Succeeded**.
5. **Failure reporting**: when a run reaches Hangfire's Failed state (retries exhausted),
   `HangfireJobFailureTelemetryFilter` sends Application Insights event `HangfireJobFailed`
   (properties `JobId`, `JobType`, `JobMethod`, `ExceptionType`, `ExceptionMessage`,
   `RecurringJobId`, `RetryCount`) plus the exception. `HangfireJobActivityFilter` wraps each run
   in Activity `Hangfire.Job.<TypeName>`. The dashboard tile `failedjobs` shows Hangfire's
   `FailedCount()`.

**Admin actions** (`RecurringJobsController`, route `api/RecurringJobs`):

| Action | Endpoint | Permission | Effect |
|---|---|---|---|
| List | `GET /api/RecurringJobs` | `Admin_Administration` read | All DB rows + category from code + `NextRunAt` |
| Detail | `GET /api/RecurringJobs/{jobName}` | `Jobs_Trigger`, `Jobs_Disable` or `Admin_Administration` | One row |
| Enable / disable | `PUT /api/RecurringJobs/{jobName}/status` | `Jobs_Disable` | Sets `IsEnabled`, `LastModifiedBy/At`. Hangfire schedule unchanged |
| Change cron | `PUT /api/RecurringJobs/{jobName}/cron` | `Admin_Administration` write | Validates with NCrontab, saves row, re-registers the Hangfire schedule live with the row's time zone |
| Run now | `POST /api/RecurringJobs/{jobName}/trigger` | `Jobs_Trigger` | Refused if the job is disabled; otherwise `BackgroundJob.Enqueue` of `ExecuteAsync` for immediate run; returns the Hangfire job id |

## Logic & formulas
- **Enabled check** (`RecurringJobStatusChecker.IsJobEnabledAsync(jobName, ct, defaultIfMissing = true)`):
  row found → its `IsEnabled`; no row → `defaultIfMissing` (jobs that cost money, e.g.
  `photobank-auto-tag`, `ecomail-sync`, `marketing-performance-refresh`, `marketing-calendar-sync`,
  pass their `DefaultIsEnabled`); **database error → true** (fail-open, so an outage never silently
  stops critical jobs).
- **Next run** (`RecurringJobNextRunCalculator`): null when disabled, when the time zone is unknown
  on the host, or when the cron is invalid; otherwise next NCrontab occurrence evaluated in the
  job's time zone, returned in UTC.
- **Cron validation**: the entity only checks 5 or 6 fields; the cron endpoint additionally parses
  with `NCrontab.Advanced` (so `99 99 * * *` is rejected there but would pass the entity check).
- **Category** is code-only metadata (`RecurringJobCategory`), resolved by job id at read time;
  rows with no matching class show as `Uncategorized`.
- **Time zone** is not editable: it comes from code on every start and is reused by the cron update.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `Hangfire:SchedulerEnabled` | `false` (class + appsettings.json); `true` only in appsettings.Production.json | Register recurring jobs with Hangfire at startup |
| `Hangfire:WorkerCount` | 1 | Parallel Hangfire workers (one server per app instance) |
| `Hangfire:SchemaName` | `hangfire_heblo` | PostgreSQL schema of Hangfire storage |
| `Hangfire:ConnectionLimit` | 5 | Max pool size of Hangfire's own Npgsql pool |
| `Hangfire:UseInMemoryStorage` | false (true in appsettings.Test.json) | In-memory storage for tests |
| `Hangfire:MaxPendingJobsPageSize` | 200 | Page cap for Hangfire monitoring queries |
| `Hangfire:RunningJobsCacheSeconds` | 2 | Cache of the running-invoice-import-jobs response |
| `ConnectionStrings:<Environment>` → `DefaultConnection` | — | Database for Hangfire storage (same DB as Heblo) |

Hard-coded storage options (`AddHangfireServices`): `QueuePollInterval` 15 s, `InvisibilityTimeout`
30 min, `DistributedLockTimeout` 10 s, `JobExpirationCheckInterval` 1 h,
`CountersAggregateInterval` 5 min.

## Runtime facts
- Production: `meta-ads-invoice-import` and `google-ads-invoice-import` had `IsEnabled = false`
  and still showed Succeeded runs in `hangfire_heblo` — agent memory
  `gotcha_adapter_recurring_job_needs_concrete_registration` — 2026-09-22.
- Production runs with one Hangfire worker; slow `plaud-polling` runs (300–440 s, every 5 min)
  delayed manual picking-list prints by 12–15 min on 2026-09-29 12:05–13:50 — agent memory
  `gotcha_hangfire_single_worker_starves_print` — 2026-09-29.
- `Heblo_TST` contains a stale `hangfire` schema (newest job 2025-11) next to the live
  `hangfire_heblo` — agent memory `gotcha_hangfire_scheduler_disabled_in_dev` — 2026-09-22.

## Known quirks
- **The enable switch is advisory.** Discovery registers every job regardless of `IsEnabled`; only
  jobs that call the status checker stop. `smartsupp-webhook-audit-cleanup`,
  `shoptet-orders-sync`, `ga4-aggregates-sync` and `flexi-analytics-sync` do not, so disabling them
  on the page changes nothing (the last three obey only their `*:Enabled` config flag).
- **Disabled ≠ not run in Hangfire.** A disabled job still fires and shows *Succeeded*. Look at the
  job's log line "is disabled. Job will be skipped" or at what it wrote, not at Hangfire state.
- **Code/config cron changes don't apply after the first seed.** The stored cron is admin-owned;
  e.g. editing `Ecomail:CronExpression` later does not move `ecomail-sync`. Change it on the page
  (or in the DB) instead.
- **`SchedulerEnabled=false` stops registration, not firing.** `AddHangfireServer` runs in every
  environment, and nothing removes recurring definitions already stored in `hangfire_heblo`. An
  instance pointed at a database whose Hangfire schema already holds definitions could fire them.
  (Read from code and Hangfire defaults, not observed.) Instances with the scheduler off still
  process enqueued one-off jobs — on a shared `Heblo_TST`, a job may run on a different instance
  than the one that enqueued it.
- **Deleted jobs are never cleaned up.** No `RecurringJob.RemoveIfExists` and no row delete: the row
  stays (Uncategorized) and Hangfire keeps firing a definition whose type can't load; the failure
  telemetry filter logs a warning if it can't describe such a job.
- **One broken job constructor can stop the app or all scheduling.** Seeding resolves every job and
  rethrows → app does not start. In `RecurringJobDiscoveryService` the `GetServices<IRecurringJob>()`
  call is outside the per-job try, so one throwing constructor leaves **all** jobs unregistered
  (logged, app keeps running). Agent memory `gotcha_kv_placeholder_defeats_isconfigured_gate`
  (2026-09-22) traced this to a non-blank Key Vault placeholder.
- **Run now refuses disabled jobs** (`RecurringJobDisabled`) and goes into the same single-worker
  queue, so it can wait behind a long-running job.
- **Manual cron for an unknown job** saves the row but logs a warning and does not touch Hangfire;
  the new cron applies at next start only if the class comes back.
- **Fail-open on DB errors**: if `RecurringJobConfigurations` can't be read, every job runs even if
  it was switched off.
- **Retries**: jobs without `[AutomaticRetry]` get Hangfire's default 10 retries; the telemetry event
  fires only after the last one.

## Code entry points
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` — `AddHangfireServices` (storage, worker, filters), `AddRecurringJobs` (assembly scan), `SeedRecurringJobConfigurationsAsync`
- `backend/src/Anela.Heblo.API/Program.cs` — startup order: migrations → seeding → failure filter → run
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs` — what is preserved vs. overwritten on start
- `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/RecurringJobDiscoveryService.cs` — Hangfire registration, `SchedulerEnabled` gate
- `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireJobRegistrationHelper.cs` — the single `AddOrUpdate` call
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobStatusChecker.cs` — enabled check, fail-open
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/UseCases/` — list, detail, status, cron, trigger handlers
- `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireRecurringJobScheduler.cs` — live cron update
- `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireJobEnqueuer.cs` — Run now
- `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireJobFailureTelemetryFilter.cs` — `HangfireJobFailed` event
- `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireSchemaInitializer.cs` — creates `hangfire_heblo`
- `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireDashboardTokenAuthorizationFilter.cs` — `/hangfire` access
- `backend/src/Anela.Heblo.Persistence/BackgroundJobs/RecurringJobConfigurationConfiguration.cs` — table mapping
