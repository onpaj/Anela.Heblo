# Architecture review: inject `TimeProvider` into the four recurring DQT jobs

## Verdict

**Approved as designed.** Every factual claim in `design-01.md` was checked against the current source and holds exactly:

- `InvoiceDqtJob.cs:44`, `StockWriteBackDqtJob.cs:44` — `DateOnly.FromDateTime(DateTime.Today.AddDays(-1))`, confirmed verbatim.
- `ProductPairingDqtJob.cs:46` — `DateOnly.FromDateTime(DateTime.Today)`, confirmed.
- `LotStockReconciliationDqtJob.cs:45` — `DateOnly.FromDateTime(DateTime.UtcNow)`, confirmed, with the from/to-equal-today comment at line 44 correctly identified as business-rule documentation, not clock-source documentation.
- `DqtYesterdayStatusTile.cs:14,25-33,39` — constructor-injects `TimeProvider`, computes `DateOnly.FromDateTime(_timeProvider.GetUtcNow().DateTime).AddDays(-1)`. This is a real, working reference pattern already in the same module — not a hypothetical.
- `DqtYesterdayStatusTileTests.cs:14-28` — the `Mock<TimeProvider>` + `.Setup(x => x.GetUtcNow()).Returns(fixedOffset)` pattern is exactly as described, and it is the only `TimeProvider`-mocking pattern in this test module. `Microsoft.Extensions.TimeProvider.Testing`/`FakeTimeProvider` is correctly ruled out as unnecessary.
- `ServiceCollectionExtensions.cs:130` (`services.AddSingleton(TimeProvider.System);` inside `AddCrossCuttingServices()`) is wired into the host at `Program.cs:108`. No DI change is needed for the four jobs to receive `TimeProvider` — confirmed, not assumed.
- The four existing test files (`InvoiceDqtJobTests.cs`, `LotStockReconciliationDqtJobTests.cs`, and by the same established pattern `StockWriteBackDqtJobTests.cs`/`ProductPairingDqtJobTests.cs`) construct the SUT with exactly `repository, jobRunner, statusChecker, logger` — the design's "add `TimeProvider` as the second-to-last constructor parameter" slots in cleanly with no other signature disruption.
- `docs/architecture/DateTime_StandardizationGuide.md` and `docs/architecture/Dev_Guidelines_time.md` both mandate UTC-only business logic and explicitly endorse `Mock`ing `TimeProvider` in tests — the design's approach is the documented standard, not an invented one.

No gap between the design document and the codebase it targets. The direction from the arch-review issue is fully addressed by this design and needs no revision.

## Alignment with existing patterns

- **Pattern reuse, not invention.** `DqtYesterdayStatusTile` already solves this exact problem in this exact module. The design's constructor-parameter position (`repository, jobRunner, statusChecker, timeProvider, logger`) mirrors the tile's `(repository, timeProvider, logger)` ordering — `timeProvider` immediately before `logger` — which is a reasonable, low-friction convention to carry over even though nothing enforces parameter order in C#.
- **Test pattern reuse.** Reusing `Mock<TimeProvider>` (Moq) rather than pulling in `Microsoft.Extensions.TimeProvider.Testing` avoids a new test dependency and keeps the four job test files consistent with the tile test file already in the same folder tree — correct call, and it closes the plan's open question definitively rather than leaving it for the implementer to re-litigate.
- **Boundary discipline.** The design correctly scopes the change to "where does the current instant come from," leaving `DqtRun.Start`, the runners (`IInvoiceDqtJobRunner`, `IDriftDqtJobRunner`), Hangfire metadata, cron expressions, and retry attributes untouched. This matches the Vertical Slice / Clean Architecture boundary already in place — jobs are thin orchestrators, comparison logic lives in the runners/comparers, and this change doesn't blur that line.

## Implementation guidance (confirmed, no changes needed)

1. Four job files get one new constructor parameter (`TimeProvider timeProvider`) and one new field (`_timeProvider`), positioned before `ILogger` per the tile's convention.
2. One-line substitution per file exactly as tabulated in `design-01.md` §1.2 — no other line changes.
3. No DI registration edit — `TimeProvider.System` singleton already resolves.
4. Four test files: add `private readonly Mock<TimeProvider> _timeProviderMock = new();`, wire `.Setup(x => x.GetUtcNow()).Returns(FixedNow)` in the constructor, pass `_timeProviderMock.Object` into the SUT constructor at the matching position, and add one dedicated fact per job asserting `DqtRun.DateFrom`/`DateTo` against a UTC-midnight-straddling fixed instant.
5. Note for the implementer: the constructor-parameter change is a breaking signature change for every existing test file that builds these SUTs (all four `*Tests.cs` files shown above construct with the old 4-arg signature) — every existing test in those files needs its constructor call updated at the same time as the production code change, not just the new date-boundary fact. The design doesn't spell this out as a discrete step, but it falls out mechanically from "add a constructor parameter" and doesn't change the design — flagging so the implementer doesn't discover it mid-build and interpret it as scope creep.

## Risks and mitigations

- **Risk: none material.** This is a mechanical, behavior-preserving-under-UTC change confined to 4 production files + 4 test files, following an established in-module pattern with an already-registered dependency. No data model, contract, or DI change.
- **Mitigation already in the design:** the boundary-straddling test cases (fixed instant at `T00:30:00Z`) are the right regression guard — they'd fail under the old `DateTime.Today` implementation if the container TZ were ever non-UTC, and they structurally can't compile against old code once the constructor signature changes, so "does this test actually guard the fix" is satisfied two ways (behavioral + structural).

## Prerequisites before implementation begins

None. `TimeProvider` DI registration exists, the reference pattern exists and is proven in production, and the test-mocking pattern exists and is proven in the same test module. Implementation can start directly from `design-01.md`.
