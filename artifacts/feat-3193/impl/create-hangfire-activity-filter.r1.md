# Implementation: create-hangfire-activity-filter

## What was implemented

Created a global Hangfire server filter `HangfireJobActivityFilter` that opens a named `Activity` per job execution using `System.Diagnostics.ActivitySource`. This allows App Insights to correlate Hangfire job telemetry under the correct `operation_Name`.

The filter:
- Uses `ActivitySource("Anela.Heblo.Hangfire")` to emit spans
- On `OnPerforming`: starts an `ActivityKind.Internal` span named `Hangfire.Job.<TypeName>`, tags it with `hangfire.job.id` and `hangfire.job.type`, and stores the activity in `context.Items`
- On `OnPerformed`: retrieves the activity, marks it as error if an exception occurred, and disposes it

The filter is registered as a `GlobalJobFilter` at the end of `AddHangfireServices`, so it applies to all Hangfire job executions automatically.

The explicit `AddActivitySourceListener` call was skipped because App Insights 2.22.0 does not expose that method on `TelemetryConfiguration`. At version 2.22, App Insights auto-subscribes to all `ActivitySource` instances, so no explicit registration is needed.

## Files created/modified

- **Created**: `backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireJobActivityFilter.cs`
- **Modified**: `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`
  - Added `GlobalJobFilters.Filters.Add(new HangfireJobActivityFilter());` at end of `AddHangfireServices`
  - No new `using` needed — `using Anela.Heblo.API.Infrastructure.Hangfire;` was already present (line 17)

## How to verify

1. Run the application and trigger any Hangfire background job.
2. Check App Insights (or local Application Insights tooling) — each job execution should appear as an `Activity` named `Hangfire.Job.<TypeName>` with tags `hangfire.job.id` and `hangfire.job.type`.
3. Verify that when a job throws an exception, the span status is set to `Error` with the exception message.

## Notes

- `AddActivitySourceListener` is not available on `TelemetryConfiguration` in App Insights 2.22.0. The auto-listen behavior at 2.22+ makes this unnecessary.
- The `using Anela.Heblo.API.Infrastructure.Hangfire;` was already present in `ServiceCollectionExtensions.cs` before this change, so no new using directive was added.
- Build: `0 errors`, 158 pre-existing warnings (unrelated to this change).

## Status
DONE
