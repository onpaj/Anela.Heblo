# Design: DB-driven CRON for Recurring Jobs

**Date:** 2026-03-29
**Status:** Approved

## Problem

CRON expressions for Hangfire recurring jobs are hardcoded in each job class's `RecurringJobMetadata`. On startup, `RecurringJobDiscoveryService` registers jobs with Hangfire using these hardcoded values — ignoring any CRON override stored in `RecurringJobConfiguration` (DB).

This means runtime CRON changes made via the management API do not survive a restart.

By contrast, `IsEnabled` is already DB-driven — it is checked at execution time against the DB, so it survives restarts correctly.

## Goal

- Make `CronExpression` in job metadata serve only as the **initial seed default**
- After seeding, the DB value is authoritative — changes survive restarts
- Users can update CRON expressions directly in the Recurring Jobs UI

## Scope

### Backend

#### 1. `RecurringJobDiscoveryService` — read CRON from DB

On `StartAsync`, after the existing seeding step (which already runs before hosted services), load all `RecurringJobConfiguration` records from DB and use `config.CronExpression` when registering each job with Hangfire.

Fallback to `metadata.CronExpression` if no DB record exists (defensive only — seeding guarantees records exist).

Update log message to indicate CRON source: `"Registered recurring job: {JobName} with schedule {Cron} (from DB)"`.

#### 2. New `UpdateRecurringJobCron` use case

New endpoint symmetric with existing `/status`:

```
PUT /api/RecurringJobs/{jobName}/cron
Body: { "cronExpression": "0 3 * * *" }
Response: { jobName, cronExpression, lastModifiedAt, lastModifiedBy }
```

Handler responsibilities:
1. Validate CRON expression format (server-side; return 400 on invalid)
2. Load `RecurringJobConfiguration` from DB, return 404 if not found
3. Update `CronExpression` in DB
4. Call `RecurringJob.AddOrUpdate` immediately — live effect without restart
5. Return updated DTO

No changes to `RecurringJobDto` — `cronExpression` field already exists.

### Frontend

#### 3. Inline CRON editing in Recurring Jobs table

The existing CRON column (read-only monospace text) gains an inline edit interaction:

- Pencil icon appears on row hover next to the CRON expression
- Click pencil → monospace input replaces text, with Save (check) and Cancel (×) buttons
- Save calls `PUT /api/RecurringJobs/{jobName}/cron`
- On success: optimistic update, invalidate jobs query
- On error: show inline error, revert to original value

Client-side validation: none (server validates, returns 400 with error message).

#### 4. New `useUpdateRecurringJobCronMutation` hook

Follows the same pattern as the existing `useUpdateRecurringJobStatusMutation`.

## What Does Not Change

- Job class files — `metadata.CronExpression` remains as seed default
- `RecurringJobMetadata` and `RecurringJobConfiguration` domain types — no changes
- Seeding logic (`SeedDefaultConfigurationsAsync`) — no changes, still seeds from metadata on first run only
- `IsEnabled` pattern — checked at execution time, already DB-driven

## Startup Sequence

```
1. SeedRecurringJobConfigurationsAsync  ← seeds DB from metadata (first run only)
2. RecurringJobDiscoveryService.StartAsync
     └─ load RecurringJobConfiguration records from DB
     └─ register each job with DB CronExpression (fallback: metadata default)
```

Step 1 already runs before hosted services (`await app.SeedRecurringJobConfigurationsAsync()` in `Program.cs` before `app.Run()`), so DB records are guaranteed present when step 2 executes.

## Files Affected

| File | Change |
|------|--------|
| `API/Infrastructure/Hangfire/RecurringJobDiscoveryService.cs` | Inject repository, load DB CRON |
| `Application/Features/BackgroundJobs/UseCases/UpdateRecurringJobCron/` | New handler, request, response |
| `API/Controllers/RecurringJobsController.cs` | New PUT /{jobName}/cron endpoint |
| `frontend/src/api/hooks/useRecurringJobs.ts` | New `useUpdateRecurringJobCronMutation` |
| `frontend/src/pages/RecurringJobsPage.tsx` | Inline edit in CRON column |
