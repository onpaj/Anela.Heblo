# Design: Unit Test Coverage for PlaudPollingJob

## Component Design

Single new test file, no production code changes:

```
backend/test/Anela.Heblo.Tests/Features/MeetingTasks/PlaudPollingJobTests.cs
```

- **Namespace:** `Anela.Heblo.Tests.Features.MeetingTasks` (matches sibling `IngestPlaudRecordingHandlerTests.cs`).
- **Class:** `public sealed class PlaudPollingJobTests`, constructing a fresh `PlaudPollingJob` and fresh mocks per test via the constructor (no shared mutable state).
- **Framework:** xUnit (`[Fact]`), Moq, FluentAssertions — matching existing conventions; no new packages.

### Mocked dependencies (constructor fixture)

```csharp
Mock<IPlaudClient>                       _mockPlaudClient
Mock<IMediator>                          _mockMediator
Mock<IRecurringJobStatusChecker>         _mockStatusChecker
IOptions<MeetingTasksOptions>            (real, via Options.Create — not mocked)
Mock<ILogger<PlaudPollingJob>>           _mockLogger
```

`_mockStatusChecker.IsJobEnabledAsync("plaud-polling", It.IsAny<CancellationToken>(), It.IsAny<bool>())` defaults to `true` in the constructor; the disabled-gate test overrides this locally.

### Test case list

1. `ExecuteAsync_WhenJobDisabled_SkipsWithoutCallingPlaudOrMediator` (FR-3)
   Gate returns `false` → `IPlaudClient.ListRecentAsync` and `IMediator.Send` are never called; method does not throw.

2. `ExecuteAsync_WhenRecordingSkippedAndNotGenerated_LogsNotGeneratedCount` (FR-4, branch 1)
   One recording; `IMediator.Send` returns `{ Skipped = true, NotGenerated = true }` → summary log reports `Ingested=0, Skipped=0, NotGenerated=1`.

3. `ExecuteAsync_WhenRecordingSkippedAndAlreadyKnown_LogsSkippedCount` (FR-4, branch 2)
   One recording; response `{ Skipped = true, NotGenerated = false }` → summary log reports `Ingested=0, Skipped=1, NotGenerated=0`.

4. `ExecuteAsync_WhenRecordingIngested_LogsIngestedCount` (FR-4, branch 3, contrast case)
   One recording; response `{ Skipped = false }` → summary log reports `Ingested=1, Skipped=0, NotGenerated=0`.

   Tests 2–4 additionally assert `IMediator.Send` was invoked exactly once with an `IngestPlaudRecordingRequest` whose `PlaudRecordingId`/`Name`/`PlaudCreatedAt` match the source `PlaudRecordingSummary`'s `Id`/`Name`/`CreatedAt`.

5. `ExecuteAsync_WhenMediatorThrowsForOneRecording_ContinuesProcessingRemainingRecordings` (FR-5)
   Two recordings with distinct IDs; `IMediator.Send` throws for the first, returns `{ Skipped = false }` for the second → method does not throw; `IMediator.Send` invoked once per recording (loop not aborted); `_mockLogger` recorded an `Error`-level log matching `"Failed to ingest recording {RecordingId}"` with the failing ID and the thrown exception; final summary log reflects only the surviving recording (`Ingested=1`), excluding the failed one from all three counters.

### Assertion idiom (per arch-review Decision 1/2)

Counter values have no assertion surface other than the rendered summary log line, so counters are verified via `Mock<ILogger<PlaudPollingJob>>.Verify(...)` on the 5-arg `Log(...)` overload, matching the exact pattern used in `LeafletIngestionJobTests.cs` (lines 334–341):

```csharp
_mockLogger.Verify(
    x => x.Log(
        LogLevel.Information,
        It.IsAny<EventId>(),
        It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(
            "0 new recordings ingested, 0 already known, 1 not yet generated")),
        It.IsAny<Exception>(),
        It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
    Times.Once);
```

The same 5-arg matcher shape is reused for the `LogError` assertion in test 5, with `LogLevel.Error` and an `It.Is<Exception>` matching the thrown exception instance.

## Data Schemas

N/A — no data schema changes; this work adds test coverage only. No new endpoints, DTOs, database schema, or event payloads are introduced or altered. `PlaudPollingJob` and all types it depends on (`IPlaudClient`, `IMediator`, `IRecurringJobStatusChecker`, `MeetingTasksOptions`, `IngestPlaudRecordingRequest`/`Response`, `PlaudRecordingSummary`) remain unmodified, per spec FR-6.
