# Architecture Review: Purchase/LowStockEfficiencyTile coverage-gap fix

## Skip Design: true

## Architectural Fit Assessment
This is a pure test-addition task with zero production code change. `LowStockEfficiencyTile.LoadDataAsync` (`backend/src/Anela.Heblo.Application/Features/Purchase/DashboardTiles/LowStockEfficiencyTile.cs`) is a small, self-contained `ITile` implementation: it sends one `GetPurchaseStockAnalysisRequest` via `IMediator`, applies a two-clause LINQ filter, and returns one of two anonymous-object shapes (`success`/`error`). It has exactly one collaborator (`IMediator`) and one incidental dependency (`TimeProvider`), both trivially mockable. There is no new module, no new interface, no new data flow — the task is to lock in existing, correct behavior with unit tests. I verified all three types the spec references against the actual source and they match the spec's description exactly:

- `GetPurchaseStockAnalysisResponse : BaseResponse` — has `Items: List<StockAnalysisItemDto>` (`backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisResponse.cs`).
- `StockAnalysisItemDto` — has `StockEfficiencyPercentage: double` and `IsConfigured: bool`, plus several other fields with no defaults that tests must populate (`ProductCode`, `ProductName`, etc. default to `string.Empty`, so minimal test objects compile fine with just the two relevant fields set).
- `BaseResponse` (`backend/src/Anela.Heblo.Application/Shared/BaseResponse.cs`) — `Success` defaults to `true` via the parameterless constructor and becomes `false` via the `BaseResponse(ErrorCodes errorCode, ...)` constructor, which `GetPurchaseStockAnalysisResponse` exposes through its own `(ErrorCodes, Dictionary<string,string>?)` constructor. So `new GetPurchaseStockAnalysisResponse(ErrorCodes.SomeCode)` is the correct, idiomatic way to produce `Success = false` for FR-3 — no need to set `Success` directly (it has a public setter, but using the constructor is the established pattern elsewhere in the codebase).

No architectural decision is required beyond "where does the test file go" and "which existing test conventions does it need to match." Both are answered by looking at sibling dashboard-tile tests.

## Proposed Architecture
### Component Overview
No new components. One new test file exercises the existing `LowStockEfficiencyTile` class through its public `LoadDataAsync` method, with `IMediator` mocked via Moq and `TimeProvider` mocked (fixed `DateTimeOffset`), exactly mirroring the constructor shape already used by sibling tiles.

### Key Design Decisions
1. **Test location diverges from the spec's stated sibling example, but for a good reason.** The spec's suggested pattern-source, `LowStockAlertTileTests.cs`, actually lives at `backend/test/Anela.Heblo.Tests/Features/Catalog/DashboardTiles/LowStockAlertTileTests.cs` — under **Catalog**, not Purchase (the brief's pointer to "wherever it actually lives" was correct to hedge). Likewise `MaterialExpirationSummaryTileTests.cs` is also under `Features/Catalog/DashboardTiles/`. Test-directory layout in this repo mirrors the production namespace path 1:1 (`backend/test/.../Features/<Module>/<SubArea>/...` matching `backend/src/.../Features/<Module>/<SubArea>/...`), so the new test must go under `Features/Purchase/DashboardTiles/`, not `Features/Catalog/DashboardTiles/`. This directory does not exist yet under `test/` (only `Features/Purchase/Infrastructure/` exists there today), so this test creates it — that's expected and correct, not a deviation.
2. **Use the JSON-serialize-then-`JsonDocument.Parse` assertion pattern**, exactly as `LowStockAlertTileTests` does, rather than reflection. It's the established convention for asserting on anonymous-object tile responses in this codebase and keeps the new test idiomatic.
3. **Use the `GetPurchaseStockAnalysisResponse(ErrorCodes, ...)` constructor for FR-3**, not manual `Success = true` overwrite. Pick any existing `ErrorCodes` member (e.g. reuse `ErrorCodes.InvalidDateRange`, already referenced by `GetPurchaseStockAnalysisHandler`, or any other existing value — the specific code is irrelevant since the tile only checks `response.Success`, not `ErrorCode`).
4. **No AutoFixture needed.** `testing-strategy.md` lists AutoFixture as available for reducing boilerplate, but `LowStockAlertTileTests` builds DTOs with plain object initializers, and the DTO surface here is tiny (2 relevant fields) — plain initializers keep intent visible and match the sibling convention more closely than AutoFixture would.

## Implementation Guidance
### Directory / Module Structure
Create:
```
backend/test/Anela.Heblo.Tests/Features/Purchase/DashboardTiles/LowStockEfficiencyTileTests.cs
```
Namespace: `Anela.Heblo.Tests.Features.Purchase.DashboardTiles` (mirrors `LowStockAlertTileTests`'s `Anela.Heblo.Tests.Features.Catalog.DashboardTiles` pattern, substituting the module).

No other files are touched. No changes to `LowStockEfficiencyTile.cs`, `GetPurchaseStockAnalysisResponse.cs`, or any handler.

### Interfaces and Contracts
No new or changed interfaces. Test-only usage of existing types:
- `LowStockEfficiencyTile(IMediator, TimeProvider)` — constructor already public, both params mockable with Moq (`Mock<IMediator>`, `Mock<TimeProvider>`).
- `_mediatorMock.Setup(x => x.Send(It.IsAny<GetPurchaseStockAnalysisRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(...)` — matches the tile's actual call (`_mediator.Send(request, cancellationToken)` where `request` is a `GetPurchaseStockAnalysisRequest`), so `It.IsAny<GetPurchaseStockAnalysisRequest>()` is correct (the tile hard-codes `StockStatus = All, IsExport = true`, so there's nothing test-relevant to match more narrowly on the request).
- `_timeProviderMock.Setup(x => x.GetUtcNow()).Returns(fixedDateTimeOffset)` — required because `LoadDataAsync` calls `_timeProvider.GetUtcNow().DateTime` twice (in `data.date` and `metadata.lastUpdated`); an unmocked `Mock<TimeProvider>` returns `default(DateTimeOffset)`, which works but a fixed non-default value is more idiomatic and matches sibling tests.

### Data Flow
Test → constructs `GetPurchaseStockAnalysisResponse` with `Items` populated in-memory → mocked `IMediator.Send` returns it → `LoadDataAsync` runs its real filter/mapping logic (no mocking of the filter itself) → test asserts on the resulting anonymous object via `JsonSerializer.Serialize` + `JsonDocument.Parse`. This is a closed loop entirely within the test process; no I/O, no ASP.NET host, no database — consistent with NFR-1/NFR-2 in the spec.

Suggested minimal test list (matches spec FR-1..FR-3, three `[Fact]`s is sufficient — no need to split further):
1. `LoadDataAsync_WithMixedEfficiencyAndConfiguration_CountsOnlyLowEfficiencyConfiguredItems` — covers FR-1 and FR-2 in one test using 4 items: below-20%-configured (counted), below-20%-unconfigured (excluded), exactly-20%-configured (excluded, boundary), above-20%-configured (excluded). Assert `count == 1` and `status == "success"`.
2. `LoadDataAsync_WhenResponseNotSuccessful_ReturnsErrorStatus` — covers FR-3.

(Two tests fully satisfy all three FRs; a third splitting FR-1/FR-2 apart is optional and not architecturally required — leave that call to the implementer/spec, not a hard requirement.)

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| New test directory `Features/Purchase/DashboardTiles/` doesn't exist yet under `test/` | Low | Standard `mkdir`-via-file-creation; no build config changes needed since the test project already globs all `.cs` files under `test/Anela.Heblo.Tests/` |
| `Mock<TimeProvider>.Setup(x => x.GetUtcNow())` on an abstract/sealed member could behave unexpectedly | Low | `LowStockAlertTileTests` already does exactly this successfully (`_timeProviderMock.Setup(x => x.GetUtcNow()).Returns(_fixedDateTime)`); proven pattern, no risk in practice |
| Spec's cited sibling test path (Purchase) doesn't match reality (it's actually under Catalog) could mislead an implementer who doesn't verify | Medium | Called out explicitly above and in Specification Amendments; implementer must place the file under `Features/Purchase/DashboardTiles/`, using `LowStockAlertTileTests` purely as a *style* reference, not a path reference |
| None — no production code risk since none is touched | N/A | N/A |

## Specification Amendments
- The spec's API/Interface Design section correctly states the new file path (`backend/test/Anela.Heblo.Tests/Features/Purchase/DashboardTiles/LowStockEfficiencyTileTests.cs`) but its narrative references to "mirroring the existing convention, e.g. `LowStockAlertTileTests`" could be misread as implying that test also lives under Purchase. Confirmed by reading the file: `LowStockAlertTileTests` and `MaterialExpirationSummaryTileTests` both live under `Features/Catalog/DashboardTiles/`, not Purchase. Implementer should use them only as a style/pattern template (mocking approach, JSON-assertion style), not as a directory precedent. No other amendments — the spec's description of `GetPurchaseStockAnalysisResponse`, `StockAnalysisItemDto`, and `BaseResponse` is accurate as written and requires no correction.

## Prerequisites
None. All referenced types already exist and compile as described; the test project already references Moq, FluentAssertions, and `System.Text.Json` (all used by `LowStockAlertTileTests`). No new NuGet packages, no schema/migration work, no config changes.
