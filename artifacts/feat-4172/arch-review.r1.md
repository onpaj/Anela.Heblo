# Architecture Review: PurchaseOrdersInTransitTile — FormatAmountInThousands test coverage

## Skip Design: true
Pure backend test-authoring task: no new or changed UI components, screens, layouts, endpoints, or visual design decisions. No production code changes are in scope. Design phase should skip UX/UI sections entirely and cover only component/test design.

## Architectural Fit Assessment
This aligns cleanly with the existing test conventions in the codebase. Dashboard tiles across every module (`Packaging`, `BackgroundJobs`, `Catalog`, `Analytics`, `Manufacture`, `Purchase`, `DataQuality`, `WeatherForecast`) already have a corresponding `*Tests.cs` file under `backend/test/Anela.Heblo.Tests/Features/<Module>/DashboardTiles/`, mirroring the source path. `PurchaseOrdersInTransitTile.cs` is the exception with no test file. The sibling `LowStockEfficiencyTileTests.cs` (same `Features/Purchase/DashboardTiles/` directory) already establishes the exact pattern needed here: constructor-inject a mocked dependency, call `LoadDataAsync()`, serialize the anonymous result via `System.Text.Json`, and assert on the parsed `JsonDocument`. No new patterns, libraries, or integration points are introduced.

The target method, `FormatAmountInThousands`, is `private` and has no side effects or external dependencies — it is pure decimal-to-string formatting. It is reachable only through the tile's single public method `LoadDataAsync`, via the `IPurchaseOrderRepository.GetByStatusAsync` result's summed `TotalAmount`. This constrains the test design (see Data Flow below) but requires no interface changes.

## Proposed Architecture

### Component Overview
```
PurchaseOrdersInTransitTileTests (new)
        |
        | mocks
        v
IPurchaseOrderRepository (existing interface, Moq)
        |
        | GetByStatusAsync(InTransit) returns IEnumerable<PurchaseOrder>
        v
PurchaseOrdersInTransitTile.LoadDataAsync()
        |
        | sums TotalAmount, calls private FormatAmountInThousands(decimal)
        v
anonymous result object { status, data: { count, totalAmount, formattedAmount }, metadata, drillDown }
        |
        | JsonSerializer.Serialize + JsonDocument.Parse (existing sibling-test pattern)
        v
FluentAssertions on doc.RootElement...GetProperty("formattedAmount")
```

No new components. One new test class; zero production-code files touched.

### Key Design Decisions

#### Decision 1: Drive the private method through `LoadDataAsync`, not via reflection
**Options considered:**
- (a) Use reflection (`typeof(...).GetMethod("FormatAmountInThousands", BindingFlags.NonPublic...)`) to invoke the private method directly with arbitrary decimals.
- (b) Drive it indirectly through `LoadDataAsync()` by controlling the mocked repository's returned `PurchaseOrder.TotalAmount`, as `LowStockEfficiencyTileTests.cs` already does for its own private/internal logic.

**Chosen approach:** (b) — indirect testing through the public entry point.

**Rationale:** Reflection-based private-method testing is not used anywhere in this codebase's existing dashboard-tile tests (confirmed by inspecting all ten sibling `*TileTests.cs` files' patterns) and is generally discouraged as it couples tests to implementation internals rather than observable behavior — consistent with `docs/architecture/testing-strategy.md`'s "Business Value Focus: Test behavior and business rules, not implementation details" principle. Driving through `LoadDataAsync()` also gives free incidental coverage of the surrounding sum/serialize logic, further help toward the 60% file coverage threshold, at no extra cost.

#### Decision 2: Construct `TotalAmount` via `PurchaseOrder.AddLine`, not a test double or reflection-based entity builder
**Options considered:**
- (a) Add a test-only factory/builder for `PurchaseOrder` that sets `TotalAmount` directly (would require a new internal setter or reflection — `TotalAmount` is a computed `_lines.Sum(...)` property with no setter).
- (b) Use the existing public constructor + `AddLine(materialId, materialName, quantity, unitPrice, notes, updatedBy)` with `quantity = 1` and `unitPrice = <target amount>`, so `LineTotal` (and thus `TotalAmount`) equals the target amount directly.

**Chosen approach:** (b).

**Rationale:** `PurchaseOrder` already exposes everything needed publicly; introducing a builder or loosening encapsulation (e.g., an internal setter visible only to tests via `InternalsVisibleTo`) is unwarranted for a single test file and would be a scope-creep change to production code, explicitly out of scope per the spec. `AddLine` requires the order to be `IsEditable` (`Status != Completed`); a freshly constructed order defaults to `Status = Draft`, which is editable, so no extra setup is needed. The mocked repository call does not care about the constructed order's `Status` field (`GetByStatusAsync(InTransit, ...)` is mocked to return whatever list the test supplies, regardless of the returned entities' actual `Status` value) — so there is no conflict between "order is Draft" and "repository call was filtered to InTransit."

#### Decision 3: One parameterized `[Theory]` for the boundary matrix, `[Fact]`s for zero and structural assertions
**Options considered:**
- (a) A single `[Theory]`/`[InlineData]` test covering every `(amount, expectedFormattedAmount)` pair from the spec, including zero.
- (b) A dedicated `[Fact]` for the zero case plus a `[Theory]` for the non-zero boundary matrix, matching the two conceptually distinct branches (`amount == 0` vs. the div/mod branches).

**Chosen approach:** (b), with the theory holding all of FR-2/FR-3/FR-4's non-zero cases (999, 1000, 1001, 1500, 5000, 9999, 10000, 999999) and one dedicated fact for the `amount == 0 -> "0"` case (FR-1) and, optionally, one for the "count == 0 for empty repo result" structural assertion, mirroring `LowStockEfficiencyTileTests`'s existing mix of a boundary-matrix-style Fact and separate structural Facts.

**Rationale:** Keeps the zero branch (which returns early and doesn't touch the `k`-suffix formatting at all) visually and semantically separate from the integer/decimal boundary matrix that is the brief's actual concern, while still satisfying the brief's "parameterised unit test" suggestion for the boundary values themselves.

## Implementation Guidance

### Directory / Module Structure
Single new file:
```
backend/test/Anela.Heblo.Tests/Features/Purchase/DashboardTiles/PurchaseOrdersInTransitTileTests.cs
```
No other files are created or modified. This mirrors the source path (`backend/src/Anela.Heblo.Application/Features/Purchase/DashboardTiles/PurchaseOrdersInTransitTile.cs`) exactly as every other tile test in the repo does.

### Interfaces and Contracts
No new or changed interfaces. Test consumes only existing public surface:
- `IPurchaseOrderRepository.GetByStatusAsync(PurchaseOrderStatus status, CancellationToken)` (mock target, via Moq).
- `PurchaseOrder` public constructor + `AddLine(...)` (test data construction).
- `PurchaseOrdersInTransitTile(IPurchaseOrderRepository)` constructor and `LoadDataAsync(...)` (system under test).

### Data Flow
1. Test arranges a `Mock<IPurchaseOrderRepository>`, sets up `GetByStatusAsync(PurchaseOrderStatus.InTransit, It.IsAny<CancellationToken>())` to return either an empty list (zero-amount case) or a single-element list containing one `PurchaseOrder` built via `new PurchaseOrder(...)` + `.AddLine(..., unitPrice: <amount>, ...)`.
2. Test constructs `new PurchaseOrdersInTransitTile(repositoryMock.Object)` and calls `await tile.LoadDataAsync()`.
3. Test serializes the returned `object` with `System.Text.Json.JsonSerializer.Serialize`, parses with `JsonDocument.Parse`, and asserts `doc.RootElement.GetProperty("data").GetProperty("formattedAmount").GetString()` equals the expected string via FluentAssertions — identical mechanics to `LowStockEfficiencyTileTests.LoadDataAsync_WithMixedEfficiencyAndConfiguration_CountsOnlyLowEfficiencyConfiguredItems`.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| `PurchaseOrder` constructor signature or `AddLine` signature drifts from what's assumed here | Low | Both were read directly from `backend/src/Anela.Heblo.Domain/Features/Purchase/PurchaseOrder.cs` as part of this review; signatures are stable public API already used elsewhere in the codebase's own test suite for other purchase-order tests. |
| `9999m / 1000m` (or similar) rounds differently than expected under `F1` formatting, producing a flaky/wrong assertion | Low | Verified analytically in the spec (Open Questions: None) — none of the chosen boundary values (999, 1001, 9999) land on an exact `.x5` midpoint, so standard round-half-away-from-zero/round-half-to-even both agree on the expected digit. No production rounding-mode change needed. |
| Coverage still falls short of 60% after adding only `FormatAmountInThousands`-focused tests, because `LoadDataAsync`'s other fields (`metadata.lastUpdated`, `drillDown.*`) remain unexercised branches-wise | Low | These are straight-line, branch-free assignments (no conditionals) that get executed as a side effect of every test case calling `LoadDataAsync()` once; line coverage (not branch coverage) is the stated metric, so they are covered incidentally without dedicated tests. |

## Specification Amendments
None — the spec's FR-1 through FR-5 are architecturally sound and require no changes.

## Prerequisites
None. No migrations, config, or infrastructure changes; the test project (`Anela.Heblo.Tests`) already references xUnit, Moq, and FluentAssertions.
