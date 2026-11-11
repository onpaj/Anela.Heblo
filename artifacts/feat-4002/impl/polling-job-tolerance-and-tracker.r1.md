# Implementation: polling-job-tolerance-and-tracker

## What was implemented
Added `PlaudPollingHealthTracker` (Interlocked singleton), `MeetingTasksOptions.UpstreamFailureAlertThreshold` (default 12), DI registration, and `PlaudPollingJob` handling: `PlaudUpstreamUnavailableException` from `ListRecentAsync` or from the per-recording mediator call ends the run quietly with a Warning, increments the tracker, and logs an Error every `threshold` consecutive failed runs (0 disables). A successful list resets the tracker. Plain exceptions in the loop keep the old continue behaviour; other list exceptions still propagate.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/PlaudPollingHealthTracker.cs` - new
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksOptions.cs` - threshold option
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs` - singleton registration
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Infrastructure/Jobs/PlaudPollingJob.cs` - tolerance logic
- `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/PlaudPollingHealthTrackerTests.cs` - new
- `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/PlaudPollingJobTests.cs` - ctor update + cases (a)-(g)

## Tests
`dotnet test backend/test/Anela.Heblo.Tests --filter FullyQualifiedName~Plaud`: 39 passed. Wider MeetingTasks filter: 170 passed, 7 failed, all Testcontainers/Docker integration tests (Docker unavailable in this environment). `dotnet format whitespace --verify-no-changes` clean.

## How to verify
Run the test command above.

## Notes
Implemented directly by the orchestrator session (no subagent Task tool available). Case (g) uses a generic InvalidOperationException to prove untyped exceptions propagate. Process doc update is the separate `process-docs-update` task.

## PR Summary
plaud-polling now treats Plaud upstream 5xx outages as transient: it logs a Warning and retries on the next tick, escalating to an Error only after a configurable number of consecutive failed runs.

### Changes
- `PlaudPollingHealthTracker.cs` - thread-safe consecutive-failure counter
- `PlaudPollingJob.cs` - tolerate `PlaudUpstreamUnavailableException`, threshold escalation
- `MeetingTasksOptions.cs` / `MeetingTasksModule.cs` - new option and DI registration
- Tests for tracker and job behaviour

## Status
DONE
