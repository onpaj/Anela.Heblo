# Plaud Upstream 5xx Handling Implementation Plan

**Goal:** Treat Plaud upstream HTTP 5xx as a typed, transient failure: warn instead of error, keep the Hangfire run green, and escalate to one Error only after a sustained outage.

**Architecture:** Typed `PlaudUpstreamUnavailableException` in Application (Services folder, because Adapter depends on Application). `PlaudCliClient` detects `API error: 5xx` in CLI stderr. `PlaudPollingJob` catches it, uses a singleton `PlaudPollingHealthTracker` and `MeetingTasks:UpstreamFailureAlertThreshold`. Docs updated in the same PR.

**Tech Stack:** .NET 8, xUnit, FluentAssertions, Moq (match the existing tests), Hangfire.

---

### task: upstream-exception-and-detection

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/PlaudUpstreamUnavailableException.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Plaud/PlaudCliClient.cs`
- Test: `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudCliClientRunTests.cs` (and `PlaudCliClientParserTests.cs` for the static helper)

- [ ] Write failing tests first: `TryParseUpstreamStatus` returns true/status for `Error: API error: 500 Internal Server Error`, 502, 503, 504; false for `API error: 404 Not Found`, `AUTH_FAILED`, empty and null. Using the existing fake-CLI approach in `PlaudCliClientRunTests`, a CLI exiting 1 with the issue's stderr throws `PlaudUpstreamUnavailableException` (StatusCode 500, message contains the trimmed stderr); `AUTH_FAILED` still throws `PlaudAuthExpiredException`; 404 stderr throws plain `InvalidOperationException` (not the new type); no token refresh is attempted on 5xx.
- [ ] Create the exception: `public sealed class PlaudUpstreamUnavailableException : InvalidOperationException` with `int StatusCode` and ctor `(int statusCode, string message)`.
- [ ] In `PlaudCliClient` add `private static readonly Regex UpstreamStatusRegex = new(@"API error:\s*(5\d\d)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);` and `public static bool TryParseUpstreamStatus(string? stderr, out int status)`.
- [ ] In `RunCliCoreAsync` restructure the non-zero-exit branch: compute `trimmed` first; `AUTH_FAILED` -> LogError + `PlaudAuthExpiredException` (unchanged); 5xx -> `LogWarning("Plaud CLI upstream error {Status}: {Error}")` + throw `PlaudUpstreamUnavailableException(status, $"Plaud CLI exited with code {code}: {trimmed}")`; else LogError + existing `InvalidOperationException`. Keep the existing message format for the unchanged branches.
- [ ] Run `dotnet test backend/test/Anela.Heblo.Adapters.Plaud.Tests`; expect all pass.
- [ ] Commit: `feat(plaud): classify upstream 5xx as PlaudUpstreamUnavailableException`.

### task: polling-job-tolerance-and-tracker

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/PlaudPollingHealthTracker.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksOptions.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Infrastructure/Jobs/PlaudPollingJob.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/PlaudPollingJobTests.cs`, create `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/PlaudPollingHealthTrackerTests.cs`

Depends on: `upstream-exception-and-detection` (needs the exception type).

- [ ] Write failing tests. Tracker: increments return 1,2,3; `Reset` returns to 0; concurrent increments (Parallel.For 1000) end at 1000. Job: (a) `ListRecentAsync` throws `PlaudUpstreamUnavailableException` -> `ExecuteAsync` does not throw, mediator never called, tracker count 1, a Warning logged; (b) threshold 2 and two consecutive failing runs -> exactly one Error log on the 2nd run, none on the 1st or 3rd; (c) threshold 0 -> no Error ever; (d) successful list resets tracker; (e) mediator throws the typed exception on the first of three recordings -> loop stops (mediator called once), no throw, tracker +1; (f) mediator throws a plain exception on the first recording -> remaining recordings still processed (existing behaviour); (g) `PlaudAuthExpiredException` from list still propagates (it lives in the adapter; the test project may need to reference it, otherwise use a generic `InvalidOperationException` to prove non-typed exceptions propagate).
- [ ] Create `PlaudPollingHealthTracker` (sealed, `Interlocked`; `RecordUpstreamFailure()` returns new count, `Reset()`, `ConsecutiveFailures`).
- [ ] Add `public int UpstreamFailureAlertThreshold { get; set; } = 12;` with XML doc to `MeetingTasksOptions`.
- [ ] Register `services.AddSingleton<PlaudPollingHealthTracker>();` in `AddMeetingTasksModule`.
- [ ] Update `PlaudPollingJob`: add ctor parameter `PlaudPollingHealthTracker tracker`; wrap `ListRecentAsync` in try/catch for the typed exception -> `HandleUpstreamFailure(ex)` and return; call `_tracker.Reset()` after a successful list; in the per-recording loop add `catch (PlaudUpstreamUnavailableException ex)` BEFORE the generic catch -> `HandleUpstreamFailure(ex)` and return. `HandleUpstreamFailure`: record failure, `LogWarning("Plaud upstream unavailable (HTTP {Status}); will retry on next tick. Consecutive failed runs: {Count}")`, and `LogError("Plaud upstream has been unavailable for {Count} consecutive runs")` when `threshold > 0 && count % threshold == 0`.
- [ ] Update the existing `new PlaudPollingJob(...)` construction in `PlaudPollingJobTests.cs` for the new parameter.
- [ ] Run `dotnet build backend` and `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~MeetingTasks"`; expect pass. Run `dotnet format backend` (whitespace verify).
- [ ] Commit: `feat(plaud): tolerate transient upstream outages in plaud-polling`.

### task: process-docs-update

**Files:**
- Modify: `docs/processes/sync-plaud-recordings.md`

Depends on: `polling-job-tolerance-and-tracker`.

- [ ] Add `MeetingTasks:UpstreamFailureAlertThreshold` (default 12, <=0 disables) to the Configuration table.
- [ ] In Known quirks, correct the "Failure counters undercount" bullet (a failure of `plaud recent` due to Plaud upstream 5xx no longer fails the run) and add a bullet: upstream `API error: 5xx` -> `PlaudUpstreamUnavailableException`, Warning log, run Succeeded, retried next tick; Error log once per threshold multiple of consecutive failing runs; counter is in-memory and resets on restart; auth and other CLI errors still fail the run.
- [ ] Add a Runtime facts line: 2026-08-29/30 Plaud API returned HTTP 500 to `plaud recent` for ~31 hours (issue #4002).
- [ ] Bump `verified_at` to the current HEAD short SHA (`git rev-parse --short=9 HEAD`) per the doc's convention.
- [ ] Run `python3 scripts/process-docs/check.py index`; expect pass.
- [ ] Commit: `docs(processes): document Plaud upstream 5xx handling`.
