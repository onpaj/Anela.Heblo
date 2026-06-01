# Next Run Column — Recurring Jobs Dashboard

**Date:** 2026-03-30
**Branch:** feat/issue-451-db-cron

## Summary

Add a "Next Run" column to the recurring jobs dashboard showing when each job will next execute. Disabled jobs show "—". The value is computed server-side using NCrontab.Advanced and returned as UTC; the frontend formats it in the user's browser timezone.

## Backend

### DTO Change

Add a nullable field to `RecurringJobDto`:

```csharp
public DateTime? NextRunAt { get; set; }
```

### Handler Change

In `GetRecurringJobsListHandler`, compute `NextRunAt` after fetching jobs from the repository, before mapping to DTOs. For enabled jobs, parse the cron expression with `NCrontab.Advanced` and call `GetNextOccurrence(DateTime.UtcNow)`. For disabled jobs, set `null`.

The AutoMapper profile gets a manual mapping override for this computed field since it cannot be derived 1:1 from the domain entity.

```csharp
// Inline in handler, no new service needed
nextRunAt = job.IsEnabled
    ? CrontabSchedule.Parse(job.CronExpression).GetNextOccurrence(DateTime.UtcNow)
    : null;
```

`DateTime.UtcNow` is injected via `TimeProvider` (already used elsewhere, or introduced here) to keep the handler unit-testable with a fixed reference time.

### Error Handling

CRON expressions in the database are already validated at write time (`UpdateRecurringJobCronHandler`). Parsing in the handler is safe; no additional error handling needed.

## Frontend

### Table Column

Add a "Next Run" column to `RecurringJobsPage.tsx`, positioned between "Last Modified" and "Status".

```tsx
{job.nextRunAt
  ? new Date(job.nextRunAt).toLocaleString()
  : '—'}
```

`toLocaleString()` uses the browser's local timezone automatically — no hardcoded timezone, no new npm dependency.

The generated API client type (`RecurringJobDto`) gains the nullable `nextRunAt?: string` field automatically on next build.

## Testing

### Backend

Add/extend tests in `GetRecurringJobsListHandlerTests.cs`:

- Enabled job with valid cron → `NextRunAt` is a future UTC `DateTime`
- Disabled job → `NextRunAt` is `null`
- Uses a fixed reference time via `TimeProvider` mock to avoid flakiness

### Frontend

Add to `useRecurringJobs.test.ts`:

- `nextRunAt` field is passed through the query result correctly (non-null for enabled, null for disabled)

## Out of Scope

- "Last Run" or execution history columns
- Live refresh / polling for next run time
- Timezone selector UI (browser timezone is sufficient)
- Hangfire internal table queries
