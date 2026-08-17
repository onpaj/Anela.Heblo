# Specification: Unit Test Coverage for PlaudPollingJob

## Summary
`PlaudPollingJob.ExecuteAsync` (`backend/src/Anela.Heblo.Application/Features/MeetingTasks/Infrastructure/Jobs/PlaudPollingJob.cs`) currently sits at 33.9% line coverage against a 60% threshold. This spec defines a self-contained unit test suite that exercises the job-enabled gate, the `Skipped`/`NotGenerated` counter branching, and per-item exception swallowing, without changing any production code.

## Background
`PlaudPollingJob` is a Hangfire recurring job (`*/5 * * * *`, disabled by default) that polls the Plaud CLI for recently completed meeting recordings and, for each one, sends an `IngestPlaudRecordingRequest` via MediatR to persist it as a proposed-task transcript. It aggregates three counters (`ingested`, `skipped`, `notGenerated`) across the batch and logs a summary line. The coverage-gap report identifies three untested code paths:

1. The early-return when `IRecurringJobStatusChecker.IsJobEnabledAsync` returns `false`.
2. The `response.Skipped && response.NotGenerated` vs. `response.Skipped && !response.NotGenerated` branch, which drives two independently-incremented counters (`notGenerated` vs. `skipped`).
3. The per-recording `try/catch` around `_mediator.Send(...)`, which must log and continue rather than abort the whole batch — critical because a single malformed recording must not silently drop every subsequent recording in that 5-minute polling cycle.

An existing unit test suite already covers `IngestPlaudRecordingHandler` (the mediator handler invoked per item) in `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/IngestPlaudRecordingHandlerTests.cs`; this spec covers the orchestrating job itself, which has no dedicated test file today.

## Functional Requirements

### FR-1: Test project, namespace, and file placement
Add a new test file `backend/test/Anela.Heblo.Tests/Features/MeetingTasks/PlaudPollingJobTests.cs` in the existing `Anela.Heblo.Tests` project, namespace `Anela.Heblo.Tests.Features.MeetingTasks` — matching the convention used by `IngestPlaudRecordingHandlerTests.cs` in the same directory and by other job test files (e.g. `Features/Packaging/FillTrackingNumbersJobTests.cs` for `Features/Packaging/Infrastructure/Jobs/FillTrackingNumbersJob.cs`), which mirror the parent feature folder rather than the `Infrastructure/Jobs` subpath.

**Acceptance criteria:**
- New file exists at the path above, in the `Anela.Heblo.Tests` project, and compiles as part of the existing test project (no new `.csproj` needed).
- Uses xUnit (`[Fact]`/`[Theory]`), `Moq` for mocking, and `FluentAssertions` for assertions — matching `IngestPlaudRecordingHandlerTests.cs` conventions.
- Test class is `public sealed class PlaudPollingJobTests`, constructing a fresh `PlaudPollingJob` (and fresh mocks) per test via the constructor (matching the existing pattern; no shared mutable state between tests).

### FR-2: Mocked dependencies
Construct `PlaudPollingJob` with mocks for all five constructor dependencies:
- `Mock<IPlaudClient>`
- `Mock<IMediator>`
- `Mock<IRecurringJobStatusChecker>`
- `IOptions<MeetingTasksOptions>` — use `Microsoft.Extensions.Options.Options.Create(new MeetingTasksOptions { ... })` (a real options wrapper, not mocked, since it's a plain settings container)
- `Mock<ILogger<PlaudPollingJob>>`

**Acceptance criteria:**
- No test hits real Plaud infrastructure, MediatR pipeline, Hangfire, or logging sinks — all collaborators are test doubles.
- `IRecurringJobStatusChecker.IsJobEnabledAsync(Metadata.JobName, ...)` is mocked to return `true` by default (arrange the disabled case explicitly per FR-3) so tests exercising downstream logic don't need to also stub the gate check away from its default.

### FR-3: Job-disabled gate test
When `IsJobEnabledAsync("plaud-polling", ..., defaultIfMissing: ...)` returns `false`, `ExecuteAsync` must return immediately without calling `IPlaudClient.ListRecentAsync` or `IMediator.Send`.

**Acceptance criteria:**
- Test `ExecuteAsync_WhenJobDisabled_SkipsWithoutCallingPlaudOrMediator` (name illustrative — pick a clear, descriptive name):
  - Arrange: `_mockStatusChecker.Setup(s => s.IsJobEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync(false)`.
  - Act: `await job.ExecuteAsync(CancellationToken.None)`.
  - Assert: `_mockPlaudClient.Verify(c => c.ListRecentAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never)`; `_mockMediator.Verify(m => m.Send(It.IsAny<IngestPlaudRecordingRequest>(), It.IsAny<CancellationToken>()), Times.Never)`.
  - The call does not throw.

### FR-4: Skipped/NotGenerated vs. Skipped/known-duplicate counter branching
The two skip sub-cases must be distinguished by the log summary counters. Since `ingested`/`skipped`/`notGenerated` are local variables with no external assertion point other than the final log message, coverage of this branch is verified indirectly by asserting on `ILogger` output content (or, if simpler and equally valid, by asserting on the *set* of mediator calls/responses driving the branch — see note below).

**Acceptance criteria:**
- Test covering `response.Skipped == true && response.NotGenerated == true`:
  - Arrange: `IPlaudClient.ListRecentAsync` returns a list with exactly one `PlaudRecordingSummary`; `IMediator.Send(It.IsAny<IngestPlaudRecordingRequest>(), ...)` returns `new IngestPlaudRecordingResponse { Skipped = true, NotGenerated = true }`.
  - Act: `await job.ExecuteAsync(CancellationToken.None)`.
  - Assert: the final summary log entry (captured via `ILogger.Log` invocation on the mock, matching the `LogInformation` call with `"{JobName} complete...` template, or by inspecting the formatted message state) reports `Ingested=0`, `Skipped=0`, `NotGenerated=1`. Use `Verify` on `_mockLogger` with a state-object predicate matching the formatted string (e.g. asserting the rendered message contains `"0 new recordings ingested, 0 already known, 1 not yet generated"`), consistent with how this codebase typically asserts logger calls (verify similar patterns in nearby job tests, e.g. `FillTrackingNumbersJobTests.cs` or `GenerateArticleJobTests.cs`, and follow whichever idiom is already used for `ILogger` verification there).
- Test covering `response.Skipped == true && response.NotGenerated == false`:
  - Same shape, but `IngestPlaudRecordingResponse { Skipped = true, NotGenerated = false }`.
  - Assert the summary reports `Ingested=0`, `Skipped=1`, `NotGenerated=0`.
- Test covering `response.Skipped == false` (ingested path), for completeness of the three-way branch (not explicitly called out as a gap but needed to fully pin the counter logic and keep the two skip-branch tests meaningful by contrast):
  - `IngestPlaudRecordingResponse { Skipped = false }`.
  - Assert the summary reports `Ingested=1`, `Skipped=0`, `NotGenerated=0`.
- All three tests assert `IMediator.Send` was called exactly once with an `IngestPlaudRecordingRequest` whose `PlaudRecordingId`/`Name`/`PlaudCreatedAt` match the fields copied from the `PlaudRecordingSummary` (`Id`, `Name`, `CreatedAt` respectively) — this also pins the request-mapping code (lines 66–71) that is otherwise untested.

### FR-5: Per-item exception swallowing
When `IMediator.Send` throws for one recording in a multi-recording batch, the exception must be caught, logged via `ILogger.LogError`, and the loop must continue processing the remaining recordings (and complete the method normally, without rethrowing).

**Acceptance criteria:**
- Test `ExecuteAsync_WhenMediatorThrowsForOneRecording_ContinuesProcessingRemainingRecordings` (name illustrative):
  - Arrange: `IPlaudClient.ListRecentAsync` returns two (or three) `PlaudRecordingSummary` items with distinct IDs. `IMediator.Send` is set up with per-recording-ID matching (e.g. `It.Is<IngestPlaudRecordingRequest>(r => r.PlaudRecordingId == "rec-1")`) to throw an `InvalidOperationException` (or similar) for the first recording, and to return a normal `IngestPlaudRecordingResponse { Skipped = false }` for the second (and third, if used).
  - Act: `await job.ExecuteAsync(CancellationToken.None)` — must not throw.
  - Assert:
    - `IMediator.Send` was invoked once for **each** recording in the batch (i.e. the failing item did not abort iteration) — verify call count equals the number of recordings, and verify explicitly that the call for the second/third recording's ID occurred.
    - `_mockLogger` recorded an `Error`-level log call whose message/template matches `"Failed to ingest recording {RecordingId}"` with the failing recording's ID, and whose exception argument is the thrown exception (verify via `Log` invocation matching `LogLevel.Error` and the exception instance, or the idiom used elsewhere in this codebase for asserting `LogError` calls — see `IngestPlaudRecordingHandlerTests.cs`/nearby job tests for the existing pattern before introducing a new one).
    - The final summary log reflects the successfully-processed recording(s) only (e.g. `Ingested=1` for the surviving one) and does not count the failed one in any of the three counters — this confirms "no corrupting global state" from the brief.

### FR-6: No production code changes
No modification to `PlaudPollingJob.cs` or any type it depends on (`IPlaudClient`, `IMediator`, `IRecurringJobStatusChecker`, `MeetingTasksOptions`, `IngestPlaudRecordingRequest`/`Response`, `PlaudRecordingSummary`) is in scope. If, while writing tests, a genuine behavioral defect is discovered (not merely an untested-but-correct path), stop and flag it rather than silently fixing it — the brief does not call out a known bug to fix.

**Acceptance criteria:**
- `git diff` for this change touches only the new test file (and no non-test files under `backend/src/`).

## Non-Functional Requirements

### NFR-1: Performance
Not applicable in the traditional sense — this is test-only work. The new tests must run fast (no real I/O, no `Task.Delay`/timers) and complete in well under 1 second total as part of the existing fast unit-test suite.

### NFR-2: Security
Not applicable — no new attack surface, no secrets or credentials involved. Test doubles only.

### NFR-3: Coverage target
Line coverage of `PlaudPollingJob.cs` must reach at least the 60% filter threshold referenced in the coverage-gap report after this change (the five test scenarios above collectively exercise every branch in `ExecuteAsync`, so 60% should be comfortably exceeded — likely near 100% excluding trivial property/constructor lines already covered by instantiation).

## Data Model
No data model changes. Relevant existing types (already defined in the codebase, not to be altered):
- `PlaudPollingJob` (class under test) — depends on `IPlaudClient`, `IMediator`, `IRecurringJobStatusChecker`, `IOptions<MeetingTasksOptions>`, `ILogger<PlaudPollingJob>`.
- `IngestPlaudRecordingRequest { PlaudRecordingId, Name, PlaudCreatedAt }` — MediatR request built per recording.
- `IngestPlaudRecordingResponse : BaseResponse { Skipped, NotGenerated, TranscriptId }` — response consumed for counter branching.
- `PlaudRecordingSummary { Id, Name, CreatedAt }` — item returned by `IPlaudClient.ListRecentAsync`.
- `MeetingTasksOptions { MaxRecordingAgeDays, ... }` — supplies the `days` argument to `ListRecentAsync`.
- `RecurringJobMetadata { JobName = "plaud-polling", ... }` — used as the key passed to `IsJobEnabledAsync`.

## API / Interface Design
Not applicable — this work adds unit tests only; no new endpoints, events, or UI.

## Dependencies
- Existing test project `Anela.Heblo.Tests` (xUnit, Moq, FluentAssertions — already referenced, per `IngestPlaudRecordingHandlerTests.cs`).
- No new NuGet packages required.
- No changes to CI configuration required beyond the tests running as part of the existing `dotnet test` / `dotnet build` pipeline already covering `backend/test/Anela.Heblo.Tests`.

## Out of Scope
- Any behavioral change to `PlaudPollingJob.cs` or its dependencies.
- Integration/E2E tests against a real or fake Plaud CLI — this is unit-test-only work with fully mocked collaborators.
- Testing `IngestPlaudRecordingHandler` internals (already covered by `IngestPlaudRecordingHandlerTests.cs`).
- Testing Hangfire scheduling/registration behavior (`RecurringJobMetadata`, `[AutomaticRetry]` attribute semantics) — out of scope for this coverage gap, which is specifically about `ExecuteAsync`'s internal branching.
- Raising the coverage threshold itself or adjusting the coverage-gap tooling configuration.

## Open Questions
None.

## Status: COMPLETE
