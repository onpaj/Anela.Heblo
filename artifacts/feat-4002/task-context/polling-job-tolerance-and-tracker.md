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

