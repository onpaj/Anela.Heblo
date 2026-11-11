# Design: Classify Plaud upstream 5xx failures as transient

## Component Design

### PlaudUpstreamUnavailableException (Application/Features/MeetingTasks/Services)
`sealed`, derives `InvalidOperationException`; ctor `(int statusCode, string message)`; property `StatusCode`.

### PlaudCliClient (Adapter)
In `RunCliCoreAsync`, on non-zero exit after computing `trimmed`:
1. `AUTH_FAILED` -> `PlaudAuthExpiredException` (unchanged, still logs Error first as today).
2. `TryParseUpstreamStatus(trimmed, out status)` -> log Warning ("Plaud CLI upstream error {Status}: {Error}") and throw `PlaudUpstreamUnavailableException(status, $"Plaud CLI exited with code {code}: {trimmed}")`.
3. Otherwise unchanged Error log + `InvalidOperationException`.
Restructure so the `LogError` call happens only for branches 1 and 3. `TryParseUpstreamStatus` is `public static`, regex `API error:\s*(5\d\d)\b`.

### PlaudPollingHealthTracker (Application)
Thread-safe consecutive-failure counter using `Interlocked.Increment` / `Interlocked.Exchange`.

### PlaudPollingJob
```
try { recordings = await ListRecentAsync }
catch (PlaudUpstreamUnavailableException ex) { HandleUpstreamFailure(ex); return; }
_tracker.Reset();
foreach recording:
  try { ... }
  catch (PlaudUpstreamUnavailableException ex) { HandleUpstreamFailure(ex); return; }
  catch (Exception ex) { existing log + continue }
```
`HandleUpstreamFailure`: `count = tracker.RecordUpstreamFailure()`; Warning "Plaud upstream unavailable (HTTP {Status}); will retry on next tick. Consecutive failed runs: {Count}"; if threshold > 0 and `count % threshold == 0` -> `LogError` "Plaud upstream has been unavailable for {Count} consecutive runs".
Note: the per-recording catch for the typed exception must precede the generic `catch (Exception)`. MediatR propagates handler exceptions unchanged, so the typed exception reaches the job (verify `IngestPlaudRecordingHandler` does not wrap it).

### Options
`MeetingTasksOptions.UpstreamFailureAlertThreshold { get; set; } = 12;` with XML doc.

## Data Schemas
No DB, API or event schema changes. Config: `MeetingTasks:UpstreamFailureAlertThreshold` (int, default 12, <=0 disables Error escalation).
