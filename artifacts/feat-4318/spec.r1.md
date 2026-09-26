# Specification: BackgroundJobs — RecurringJobSeeder must not overwrite audit fields when nothing changed

## Summary

`RecurringJobSeeder.SeedDefaultConfigurationsAsync` unconditionally calls
`RecurringJobConfiguration.UpdateConfiguration(...)` for every already-existing job row on every
application startup, even when the seeded (developer-owned) fields — `DisplayName`, `Description`,
`TimeZoneId` — are byte-for-byte identical to what is already stored. `UpdateConfiguration` always
stamps `LastModifiedAt = now` and `LastModifiedBy = "System"`, so a genuine admin change made
yesterday (e.g. editing the CRON expression or disabling a job) has its audit trail silently
overwritten on the next restart. This spec defines a guard so the seeder only calls
`UpdateConfiguration` when a developer-owned field has actually changed, preserving the existing
row's `LastModifiedAt`/`LastModifiedBy` otherwise.

## Background

`RecurringJobConfiguration` rows are the single source of truth for recurring-job scheduling and are
displayed in the Recurring Jobs admin UI, including `LastModifiedAt`/`LastModifiedBy` as an audit
trail for admin actions (enabling/disabling a job, editing its CRON expression). Three write paths
touch a row:

1. **`RecurringJobSeeder`** (this spec's subject) — runs at every application startup, discovers
   `IRecurringJob` implementations, and syncs developer-owned fields (`DisplayName`, `Description`,
   `TimeZoneId`) from code metadata into the DB row, explicitly preserving the admin-owned
   `CronExpression` (already done correctly — see `RecurringJobSeeder.cs:53`, `existing.CronExpression`
   is passed back into `UpdateConfiguration` unchanged) and `IsEnabled` (untouched, since
   `UpdateConfiguration` has no `isEnabled` parameter).
2. **`UpdateRecurringJobCronHandler`** — admin-initiated CRON edits, via `UpdateCronExpression(...)`.
3. **`UpdateRecurringJobStatusHandler`** — admin-initiated enable/disable, via `Enable(...)` /
   `Disable(...)`.

The seeder's XML doc comment already states the intent: "updates the developer-owned fields
(`DisplayName`, `Description`) to match the current code, while preserving the admin-owned fields
(`CronExpression`, `IsEnabled`) exactly as stored." The code honors that intent for `CronExpression`
and `IsEnabled` themselves, but not for the audit fields that are supposed to record *only* admin
changes: today, `LastModifiedAt`/`LastModifiedBy` are stamped by the seeder on every restart
regardless of whether any developer-owned field actually changed, because
`RecurringJobConfiguration.UpdateConfiguration(...)` is unconditionally invoked (there is no
change-detection before the call) and that method unconditionally sets
`LastModifiedAt`/`LastModifiedBy` as a side effect of updating the developer-owned fields.

Consequence: after every deploy/restart, every existing job row's "Last modified by / at" flips to
`System` / restart-time in the UI, even for jobs an admin customized yesterday and even for jobs
where nothing seeded actually differs from what's stored. This destroys the audit trail the fields
exist to provide.

## Functional Requirements

### FR-1: Seeder only updates a row when a developer-owned field actually differs

For each discovered job with an existing `RecurringJobConfiguration` row, the seeder must compare
the three developer-owned fields — `DisplayName`, `Description`, `TimeZoneId` — between the freshly
computed metadata and the stored row using ordinal string equality. `UpdateConfiguration(...)` (and
therefore the `LastModifiedAt`/`LastModifiedBy` stamp and the repository `UpdateAsync` write) must be
invoked **only if at least one of the three differs**. When all three are identical to what's stored,
the seeder must not call `UpdateConfiguration` and must not call `_repository.UpdateAsync` for that
row at all.

**Acceptance criteria:**
- Given an existing row with `DisplayName`/`Description`/`TimeZoneId` matching current job metadata
  exactly, and `LastModifiedBy = "Admin"` / `LastModifiedAt = T0` (simulating a prior admin CRON or
  enable/disable edit), when the seeder runs, then the row's `LastModifiedBy` remains `"Admin"` and
  `LastModifiedAt` remains `T0` after the run (unchanged).
- Given the same setup but with `Description` differing from current metadata, when the seeder runs,
  then `Description` is updated to the metadata value, and `LastModifiedBy`/`LastModifiedAt` are
  updated to `"System"` / the current seed time (existing behavior, preserved for the changed case).
- Given the same setup with only `TimeZoneId` differing, when the seeder runs, then `TimeZoneId` is
  resynced and `LastModifiedBy`/`LastModifiedAt` are stamped to `"System"` / current seed time.
- Given a new job with no existing row, the seeder still creates it via `AddAsync` exactly as today —
  this requirement only changes the update-existing-row branch.
- `CronExpression` and `IsEnabled` continue to be excluded from the change comparison and continue to
  be preserved exactly as stored in all cases (no regression to existing behavior).

### FR-2: No change in behavior for the "something changed" case

When the guard determines an update is needed, the resulting row state (which fields are updated
to which values, which are preserved) must be identical to current behavior: `DisplayName`,
`Description`, `TimeZoneId` set from metadata; `CronExpression` preserved from the existing row;
`LastModifiedBy = "System"`; `LastModifiedAt = now` (from `TimeProvider`).

**Acceptance criteria:**
- All four existing `RecurringJobSeederTests` cases that exercise an actual field change (currently:
  `SeedDefaultConfigurationsAsync_WhenConfigurationExists_UpdatesDisplayNameAndDescription`,
  `SeedDefaultConfigurationsAsync_WhenConfigurationExists_PreservesCronExpressionAndIsEnabled`,
  `SeedDefaultConfigurationsAsync_WhenConfigurationExists_ResyncsTimeZoneIdFromMetadata`) continue to
  pass unmodified in their assertions (their fixtures already seed a stale `DisplayName`/`Description`
  or `TimeZoneId`, so the guard is triggered).

## Non-Functional Requirements

### NFR-1: Performance

No new I/O is introduced. The comparison is an in-memory string comparison against the entity
already fetched by the existing `GetByJobNameAsync` call; skipping `UpdateAsync` for unchanged rows
reduces DB writes on every restart (currently N writes per restart where N = number of existing job
rows; after the fix, 0 writes when nothing changed).

### NFR-2: Security

No auth/authorization surface changes. No new user input is introduced; `DisplayName`/`Description`/
`TimeZoneId` continue to originate only from compiled-in `IRecurringJob.Metadata`, never from
end-user input.

## Data Model

No schema change. `RecurringJobConfiguration` (`backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobConfiguration.cs`)
is unchanged: `JobName` (PK), `DisplayName`, `Description`, `CronExpression`, `TimeZoneId`,
`IsEnabled`, `LastModifiedAt`, `LastModifiedBy`. No new migration required.

## API / Interface Design

No public API/controller/DTO changes. The fix is internal to
`RecurringJobSeeder.SeedDefaultConfigurationsAsync` (backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs).
No new public method is strictly required, but the architecture review may recommend how the
change-check is expressed (inline comparison in the seeder vs. a small helper).

## Dependencies

- `IRecurringJobConfigurationRepository` (existing) — no interface change needed.
- `TimeProvider` (existing) — still only consulted when a write actually happens (or still computed
  upfront as today; behavior for the "no write" branch must not read/require `now` for anything
  other than the potential write).
- Existing test file `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobSeederTests.cs`
  must be updated: the current test
  `SeedDefaultConfigurationsAsync_WhenConfigurationExists_SetsLastModifiedByToSystem` asserts today's
  buggy behavior (that `LastModifiedBy` always flips to `"System"` even when nothing else changed,
  starting from `"Admin"` with all other fields identical to metadata). That test's premise
  contradicts FR-1 and must be corrected as part of this change: it should assert that
  `LastModifiedBy` remains `"Admin"` (unchanged) when no developer-owned field differs, and a new
  test should confirm `LastModifiedBy` becomes `"System"` when a developer-owned field does differ
  (this second case is already covered indirectly by
  `SeedDefaultConfigurationsAsync_WhenConfigurationExists_UpdatesDisplayNameAndDescription`, which
  does not currently assert `LastModifiedBy`/`LastModifiedAt` — consider strengthening it or adding a
  dedicated assertion).

## Out of Scope

- Any change to `UpdateRecurringJobCronHandler` or `UpdateRecurringJobStatusHandler` (admin write
  paths) — they are unaffected and already stamp audit fields correctly on genuine admin actions.
- Any change to how `CronExpression`/`IsEnabled` are preserved — already correct, not touched.
- Any UI change to the Recurring Jobs page — the audit fields' display is already correct; only the
  data being written is being fixed.
- Introducing a general-purpose "dirty tracking" / change-detection abstraction for other entities —
  scoped to this one seeder method only.

## Open Questions

None.

## Status: COMPLETE
