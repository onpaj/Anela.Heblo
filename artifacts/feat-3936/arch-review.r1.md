# Architecture Review: Unit Test Coverage for PlaudPollingJob

## Skip Design: true

This is a pure backend unit-test addition with no production code change (spec FR-6, confirmed against `PlaudPollingJob.cs`) and no new or altered UI surface. There is no design work to skip past — the spec itself explicitly excludes any API/interface/data-model change. Verified: no controller, DTO, or React component is touched by this work.

## Architectural Fit Assessment

This slots into an already-established convention with no ambiguity. The codebase has ~20 `IRecurringJob` implementations across feature folders, several with the identical shape to `PlaudPollingJob` (list items → per-item mediator `Send` in a `try/catch` loop → aggregate counters → single summary log). Two of them are essentially structural siblings and already have test coverage that this spec should mirror rather than reinvent:

- `Features/Leaflet/Infrastructure/Jobs/LeafletIngestionJob.cs` / `LeafletIngestionJobTests.cs` — same shape: `IRecurringJobStatusChecker` gate, `IMediator.Send` per item, per-item `try/catch`, continue-on-error.
- `Features/Packaging/Infrastructure/Jobs/FillTrackingNumbersJob.cs` / `FillTrackingNumbersJobTests.cs` — same gate pattern, per-item exception swallow verified via `Verify(..., Times.Never)` on the downstream call for the failed item.

No new abstractions, packages, or test infrastructure are needed. This is as low-risk architecturally as a coverage-gap ticket gets — the only judgment calls are (a) which test-file location convention to follow (already resolved by the analyst against `FillTrackingNumbersJobTests.cs`) and (b) how to assert the two-counter branch that has no externally observable state (a real gap in the spec's guidance — see Decision 1 below).

## Proposed Architecture

### Component Overview

No new components. Test-only addition:

```
backend/test/Anela.Heblo.Tests/Features/MeetingTasks/
├── IngestPlaudRecordingHandlerTests.cs   (existing — handler-level tests)
└── PlaudPollingJobTests.cs                (new — orchestration-level tests)
```

```
PlaudPollingJobTests (new)
        │
        ▼  constructs SUT with 5 mocked collaborators
PlaudPollingJob.ExecuteAsync
        │
        ├─► Mock<IRecurringJobStatusChecker>.IsJobEnabledAsync   (gate)
        ├─► Mock<IPlaudClient>.ListRecentAsync                    (source list)
        ├─► Mock<IMediator>.Send(IngestPlaudRecordingRequest)     (per item, mocked — NOT the real handler)
        └─► Mock<ILogger<PlaudPollingJob>>.Log                    (summary + per-item error — assertion surface)
```

Note the mediator is mocked, not the real `IngestPlaudRecordingHandler` — `IngestPlaudRecordingHandlerTests.cs` already owns handler-internals coverage (spec explicitly scopes this out). Keeping the mediator mocked is also what makes this a true unit test of the orchestration logic rather than an integration test in disguise.

### Key Design Decisions

#### Decision 1: How to assert the `ingested`/`skipped`/`notGenerated` counters

**Options considered:**
1. Assert via `Mock<ILogger>.Verify(x => x.Log(...))` matching the formatted summary string, per FR-4's suggestion.
2. Refactor `PlaudPollingJob` to return a result object with the three counts, making them directly assertable (rejected — FR-6 explicitly forbids production changes).
3. Assert indirectly by counting `Mediator.Send` invocations whose input/response shape corresponds to each branch (partial coverage only — proves the request was sent with a given response, not that the *job* incremented the right counter).

**Chosen approach:** Option 1, using the exact idiom already established in `LeafletIngestionJobTests.cs` (`Execute_logs_warning_and_continues_when_UpdateSourcePathAsync_throws`, lines 334–341):

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

**Rationale:** This is a real, already-in-use codebase idiom for exactly this situation (local counters with no other observation point) — not an invented pattern. FR-4 gestures at "the idiom used elsewhere" without naming it precisely; `LeafletIngestionJobTests.cs` is the concrete precedent and should be cited directly in the test file (a comment pointing to it is optional but the *pattern* must match exactly, including the five-argument `Log` overload signature — a common mistake is omitting the `Func<It.IsAnyType, Exception?, string>` matcher, which causes the `Verify` to silently never match under Moq's strict overload resolution).

One correction to FR-4's suggested substring: the actual log template in `PlaudPollingJob.cs` line 93–95 is:
```
"{JobName} complete. {Ingested} new recordings ingested, {Skipped} already known, {NotGenerated} not yet generated"
```
FR-4's suggested assertion text matches this correctly for the `Ingested=0, Skipped=0, NotGenerated=1` case — implementers should copy the exact rendered string per test case rather than paraphrase, since `.Contains()` matching is exact-substring.

#### Decision 2: `ILogger` mock vs `NullLogger`

**Options considered:** `NullLogger<PlaudPollingJob>.Instance` (used in `GenerateArticleJobTests.cs`, `FillTrackingNumbersJobTests.cs`) vs. `Mock<ILogger<PlaudPollingJob>>` (used in `LeafletIngestionJobTests.cs`).

**Chosen approach:** `Mock<ILogger<PlaudPollingJob>>`, per spec FR-2 — this is correctly specified because it is required to satisfy Decision 1 (the counters have no other assertion surface). Jobs that expose their outcome through repository/client call arguments (`FillTrackingNumbersJobTests.cs`) can get away with `NullLogger`; `PlaudPollingJob` cannot, because the counters are truly local. Do not default to `NullLogger` here even though it's the more common pattern in the codebase — it would make FR-4 unimplementable.

#### Decision 3: Per-item exception test — number of recordings

**Options considered:** 2-item batch (fail first, succeed second) vs. 3-item batch (fail middle, succeed both flanks).

**Chosen approach:** 2-item batch, matching `FillTrackingNumbersJobTests.cs::ExecuteAsync_ContinuesProcessing_WhenShoptetThrowsForOneOrder` and `LeafletIngestionJobTests.cs::Execute_continues_after_single_file_failure`. The spec allows "two (or three)" — two is sufficient to prove "the loop doesn't abort" and is the established minimal-batch idiom in this codebase; a 3rd item adds no additional branch coverage.

## Implementation Guidance

### Directory / Module Structure

Single new file, confirmed by direct inspection — no ambiguity:

```
backend/test/Anela.Heblo.Tests/Features/MeetingTasks/PlaudPollingJobTests.cs
```

- Namespace: `Anela.Heblo.Tests.Features.MeetingTasks` (matches sibling `IngestPlaudRecordingHandlerTests.cs` in the same directory).
- No `.csproj` change — `Anela.Heblo.Tests` already has `xunit`, `Moq`, and `FluentAssertions` referenced (confirmed via existing usages in the same directory).
- Class shape: `public sealed class PlaudPollingJobTests`, matching `IngestPlaudRecordingHandlerTests` (`sealed`) rather than the unsealed `LeafletIngestionJobTests`/`FillTrackingNumbersJobTests` — FR-1 explicitly calls for `sealed`, follow the spec here even though it's a minor deviation from the two structurally-closest job tests.

### Interfaces and Contracts

No new interfaces. Constructor dependencies to mock (all already exist, confirmed by reading `PlaudPollingJob.cs` and its dependency types):

```csharp
IPlaudClient                          // .ListRecentAsync(int days, CancellationToken) -> Task<List<PlaudRecordingSummary>>
IMediator                             // .Send(IngestPlaudRecordingRequest, CancellationToken) -> Task<IngestPlaudRecordingResponse>
IRecurringJobStatusChecker            // .IsJobEnabledAsync(string jobName, CancellationToken, bool defaultIfMissing = true) -> Task<bool>
IOptions<MeetingTasksOptions>         // real Options.Create(...), not mocked
ILogger<PlaudPollingJob>              // Mock, per Decision 2
```

`PlaudRecordingSummary { Id, Name, CreatedAt }` and `IngestPlaudRecordingResponse : BaseResponse { Skipped, NotGenerated, TranscriptId }` are both plain classes (per the project's DTO-as-class rule) — construct them with object initializers, no builder needed.

Constructor-mock pattern to follow (adapted from `LeafletIngestionJobTests.cs`'s `CreateJob()` helper, which is preferable to `FillTrackingNumbersJobTests.cs`'s tuple-returning static factory since it more directly matches this spec's "fresh SUT per test via the constructor" requirement in FR-1):

```csharp
public sealed class PlaudPollingJobTests
{
    private readonly Mock<IPlaudClient> _mockPlaudClient = new();
    private readonly Mock<IMediator> _mockMediator = new();
    private readonly Mock<IRecurringJobStatusChecker> _mockStatusChecker = new();
    private readonly Mock<ILogger<PlaudPollingJob>> _mockLogger = new();
    private readonly PlaudPollingJob _job;

    public PlaudPollingJobTests()
    {
        _mockStatusChecker
            .Setup(s => s.IsJobEnabledAsync("plaud-polling", It.IsAny<CancellationToken>(), true))
            .ReturnsAsync(true);

        _job = new PlaudPollingJob(
            _mockPlaudClient.Object,
            _mockMediator.Object,
            _mockStatusChecker.Object,
            Options.Create(new MeetingTasksOptions { MaxRecordingAgeDays = 7 }),
            _mockLogger.Object);
    }
}
```

Note: setting up the default `IsJobEnabledAsync` in the constructor (matching `true` at the two-arg call site's implicit `defaultIfMissing: true` default) satisfies FR-2's requirement without every downstream test needing its own arrange step; the FR-3 disabled test overrides it locally with its own `.Setup(...)` call, which Moq resolves as the most-specific/most-recent matching setup.

### Data Flow

Standard AAA per test, no cross-test shared mutable state (FR-1):

1. **Arrange**: stub `IPlaudClient.ListRecentAsync` to return 0–3 `PlaudRecordingSummary` items; stub `IMediator.Send` per-recording-ID (using `It.Is<IngestPlaudRecordingRequest>(r => r.PlaudRecordingId == "...")`) to return/throw as the scenario needs.
2. **Act**: `await _job.ExecuteAsync(CancellationToken.None)`.
3. **Assert**: a combination of `Mock.Verify(...)` call-count/argument checks on `_mockPlaudClient`/`_mockMediator`, and `_mockLogger.Verify(x => x.Log(...))` for the summary-counter and per-item-error cases per Decision 1.

Five scenarios per spec FR-3/FR-4/FR-5 map to five (or six, counting the three-way FR-4 split as three tests) `[Fact]` methods — no `[Theory]` needed since each scenario has materially different arrange/assert shape, consistent with how `IngestPlaudRecordingHandlerTests.cs` and `LeafletIngestionJobTests.cs` are both organized as flat `[Fact]` lists rather than parameterized.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `Mock<ILogger>.Verify` with the 5-arg `Log(...)` overload is easy to get subtly wrong (wrong arg count/order silently never matches, giving a false negative that manifests as a failing test rather than a compile error) | Medium | Copy the exact matcher shape from `LeafletIngestionJobTests.cs` lines 334–341 verbatim; do not hand-roll it |
| Log-message-substring assertions are brittle to unrelated wording changes in `PlaudPollingJob.cs`'s log templates | Low | Accepted trade-off — it's the only assertion surface for local counters without a production change (FR-6 forbids adding one); scope the `.Contains()` match to the smallest substring that pins ​the counter values, not the whole sentence, to reduce (not eliminate) fragility |
| Coverage tool may still show <100% on the `catch` block's log line if the exception-message overload isn't exercised exactly | Low | Not a blocker — NFR-3 only requires ≥60%; the five scenarios comfortably clear that regardless of a stray uncovered line |

No risk items rise above Low/Medium — this is a self-contained, low-blast-radius change.

## Specification Amendments

The spec is implementation-ready as written. Two small clarifications worth folding in before/while implementing (neither changes scope or effort):

1. **FR-4's suggested log-assertion idiom should point at `LeafletIngestionJobTests.cs` specifically**, not "nearby job tests" generically — it's the only sibling test file in this codebase using `Mock<ILogger>.Verify(...)` on a job with the identical gate/loop/catch/summary-log shape as `PlaudPollingJob`. `FillTrackingNumbersJobTests.cs` and `GenerateArticleJobTests.cs` (also referenced by the spec) both use `NullLogger` and verify outcomes through repository/client call arguments instead — they do **not** demonstrate the logger-assertion idiom FR-4 actually needs, and following them here would leave FR-4 unimplementable.
2. **FR-3's mention of `IsJobEnabledAsync("plaud-polling", ..., defaultIfMissing: ...)` should be read as the 3-parameter interface signature** (`jobName`, `cancellationToken`, `defaultIfMissing = true`) — the production call site at `PlaudPollingJob.cs:45` only passes 2 args and relies on the `defaultIfMissing: true` default. Tests should mock `IsJobEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())` (matching all three params, as FR-3's own acceptance criteria already correctly specifies) — this is already right in the spec, called out here only to confirm it against the real interface (`IRecurringJobStatusChecker.cs`) since the brief's phrasing was slightly ambiguous.

No functional, data-model, or contract changes are needed.

## Prerequisites

None. No migrations, no config, no new packages, no infrastructure changes. The test project already builds and references everything required; the developer can start directly on `PlaudPollingJobTests.cs`.
