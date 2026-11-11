# Architecture Review: Classify Plaud upstream 5xx failures as transient

## Skip Design: true
Backend-only change (adapter, background job, options, docs). No UI.

## Architectural Fit Assessment
Fits the existing pattern set by `PlaudAuthExpiredException`: the adapter (`PlaudCliClient`) translates CLI stderr into typed exceptions and the polling job decides policy. Verified in code: `Anela.Heblo.Adapters.Plaud` references the Application layer (it implements `IPlaudClient` from `Application/Features/MeetingTasks/Services`), so `PlaudPollingJob` (Application) cannot reference an adapter-defined exception. The spec's interface note is therefore resolved as follows: the new exception must be defined in the Application project, beside `IPlaudClient`. (`PlaudAuthExpiredException` is in the adapter and is not caught by the job; it is not moved.) `PlaudPollingJob` is auto-registered by the `IRecurringJob` scan; `MeetingTasksModule.AddMeetingTasksModule` is where the tracker singleton is registered.

## Proposed Architecture

### Component Overview
```
plaud CLI --stderr "API error: 503 ..."--> PlaudCliClient.RunCliCoreAsync (Adapter)
      throws PlaudUpstreamUnavailableException (Application/Services)
              |
              v
PlaudPollingJob (Application) --catch--> Warning log + PlaudPollingHealthTracker.RecordUpstreamFailure()
                                         -> Error log at threshold multiples
              success of ListRecentAsync --> tracker.Reset()
```

### Key Design Decisions

#### Decision 1: Exception placement
**Options:** adapter project (like auth exception) vs Application `Services` folder.
**Chosen:** `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/PlaudUpstreamUnavailableException.cs`.
**Rationale:** the consumer (job) lives in Application and the dependency direction forbids Application -> Adapter. Derive from `InvalidOperationException` for backwards compatibility.

#### Decision 2: Detection
**Chosen:** regex `API error:\s*(5\d\d)\b` (compiled, `RegexOptions.CultureInvariant`) over trimmed stderr, evaluated after the `AUTH_FAILED` check. Expose a `public static bool TryParseUpstreamStatus(string stderr, out int status)` helper on `PlaudCliClient` for direct unit testing.
**Rationale:** the CLI's observed stable text is `Error: API error: 500 Internal Server Error`. Matching only 5xx keeps 4xx as genuine defects.

#### Decision 3: Policy in job, not client
**Chosen:** no retry/backoff in the client. The job swallows the exception per run; the 5-minute cron is the retry. A persisted circuit breaker is unnecessary at this cadence.

#### Decision 4: Sustained-outage escalation via in-memory singleton
**Chosen:** `PlaudPollingHealthTracker` (Application/Features/MeetingTasks/Services), `Interlocked`-based, registered `AddSingleton` in `MeetingTasksModule`. Methods: `int RecordUpstreamFailure()` (returns new consecutive count), `void Reset()`. Job logs Error when `threshold > 0 && count % threshold == 0`. Option `MeetingTasksOptions.UpstreamFailureAlertThreshold` default 12.
**Rationale:** Hangfire runs a single worker in a single process; process-local state is enough, no DB migration (migrations are manual in this project).

#### Decision 5: Reset semantics
Reset only after a successful `ListRecentAsync`. If the list succeeds but a per-recording call hits upstream 5xx, the loop stops with a Warning and the tracker records a failure (outage affects ingestion).

## Implementation Guidance

### Directory / Module Structure
- Create `Application/Features/MeetingTasks/Services/PlaudUpstreamUnavailableException.cs`
- Create `Application/Features/MeetingTasks/Services/PlaudPollingHealthTracker.cs`
- Modify `Adapters/Anela.Heblo.Adapters.Plaud/PlaudCliClient.cs` (detection, log level)
- Modify `Application/Features/MeetingTasks/Infrastructure/Jobs/PlaudPollingJob.cs` (new ctor param `PlaudPollingHealthTracker`, catch logic)
- Modify `Application/Features/MeetingTasks/MeetingTasksOptions.cs`, `MeetingTasksModule.cs`
- Tests: extend `backend/test/Anela.Heblo.Adapters.Plaud.Tests/PlaudCliClientRunTests.cs`; `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/PlaudPollingJobTests.cs` (existing ctor calls must be updated for the new parameter); new `PlaudPollingHealthTrackerTests.cs`.
- Docs: `docs/processes/sync-plaud-recordings.md`.

### Interfaces and Contracts
```csharp
public sealed class PlaudUpstreamUnavailableException : InvalidOperationException
{
    public int StatusCode { get; }
    public PlaudUpstreamUnavailableException(int statusCode, string message);
}
public sealed class PlaudPollingHealthTracker
{
    public int RecordUpstreamFailure();
    public void Reset();
    public int ConsecutiveFailures { get; }
}
```

### Data Flow
Cron tick -> job -> `ListRecentAsync` -> CLI exits 1 with 5xx stderr -> typed exception -> job Warning, tracker +1 (Error at threshold multiples) -> run Succeeded. Healthy tick -> tracker reset.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| CLI changes its error text so regex misses | Low | Falls back to current behaviour (plain `InvalidOperationException`); no regression |
| Swallowing hides outage from Hangfire dashboard | Medium | Error escalation after threshold; Warning logs remain in App Insights |
| Process restart resets counter | Low | Accepted; documented |
| Existing `PlaudPollingJobTests` break on ctor change | Low | Update tests in same task |

## Specification Amendments
- Exception lives in the Application project (not the adapter) due to dependency direction.
- FR-4 counts a per-recording upstream failure as a failure too (Decision 5).

## Prerequisites
None (no migration, no config required; the new option has a default).
