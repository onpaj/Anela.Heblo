# Specification: PurchaseOrdersInTransitTile — FormatAmountInThousands test coverage

## Summary
`PurchaseOrdersInTransitTile.FormatAmountInThousands` has 0% line coverage against a 60% threshold. The method has three untested branches (zero, integer-thousands, decimal-thousands) with an unverified integer/decimal boundary. This spec defines the unit test coverage needed to close the gap and lock in the intended formatting behavior.

## Background
`PurchaseOrdersInTransitTile` is a dashboard tile (`backend/src/Anela.Heblo.Application/Features/Purchase/DashboardTiles/PurchaseOrdersInTransitTile.cs`) that sums the `TotalAmount` of all purchase orders in `InTransit` status and renders it as a compact "Xk" string via the private method `FormatAmountInThousands(decimal amount)`:

```csharp
private string FormatAmountInThousands(decimal amount)
{
    if (amount == 0)
        return "0";

    var amountInThousands = amount / 1000m;

    if (amountInThousands % 1 == 0)
        return $"{(int)amountInThousands}k";
    else
        return $"{amountInThousands:F1}k";
}
```

Because the method is `private`, it can only be exercised indirectly through the tile's public `LoadDataAsync` entry point, by controlling the total amount returned by a mocked `IPurchaseOrderRepository`. This is the same pattern already used by the sibling test `backend/test/Anela.Heblo.Tests/Features/Purchase/DashboardTiles/LowStockEfficiencyTileTests.cs`.

No test file exists yet for this tile (`backend/test/Anela.Heblo.Tests/Features/Purchase/DashboardTiles/PurchaseOrdersInTransitTileTests.cs` is absent).

## Functional Requirements

### FR-1: Cover the zero-amount branch
`FormatAmountInThousands(0)` must return exactly `"0"` (not `"0k"`).

**Acceptance criteria:**
- With `IPurchaseOrderRepository.GetByStatusAsync` returning an empty order list (or orders totalling exactly 0), `LoadDataAsync().data.formattedAmount` equals `"0"`.

### FR-2: Cover the integer-thousands branch
When `amount / 1000` has no fractional component, the result is an integer followed by `k`, with no decimal point.

**Acceptance criteria:**
- `amount = 1000` -> `"1k"`
- `amount = 5000` -> `"5k"`
- `amount = 10000` -> `"10k"`

### FR-3: Cover the decimal-thousands branch
When `amount / 1000` has a fractional component, the result is formatted to exactly one decimal place followed by `k`.

**Acceptance criteria:**
- `amount = 1500` -> `"1.5k"`
- `amount = 9999` -> `"10.0k"` (9999/1000 = 9.999m; `F1` rounds to the nearest tenth, and 9.999 is closer to 10.0 than to 9.9, so this is not a midpoint-rounding edge case)
- `amount = 999999` -> `"1000.0k"`

### FR-4: Cover the integer/decimal boundary explicitly
The boundary between FR-2 and FR-3 is the primary regression risk named in the coverage-gap brief: values just below and at a round-thousand boundary must not cross into the wrong branch.

**Acceptance criteria:**
- `amount = 999` -> `"1.0k"` (999/1000 = 0.999, not exactly divisible by 1 → decimal branch)
- `amount = 1000` -> `"1k"` (exactly divisible → integer branch)
- `amount = 1001` -> `"1.0k"` (1001/1000 = 1.001 → decimal branch; F1 rounds to `1.0k`)

### FR-5: Test via the public entry point, one order per scenario
Because `FormatAmountInThousands` is private, each test case drives it through `LoadDataAsync()`:
1. Mock `IPurchaseOrderRepository.GetByStatusAsync(PurchaseOrderStatus.InTransit, ...)` to return a list containing zero or one `PurchaseOrder` whose `TotalAmount` equals the scenario's target amount.
2. A non-zero `TotalAmount` is achieved by constructing a `PurchaseOrder` via its public constructor and calling `AddLine(materialId, materialName, quantity: 1, unitPrice: <target amount>, notes: null, updatedBy: "test")` — `TotalAmount` is a computed sum of `PurchaseOrderLine.LineTotal`, so it cannot be set directly.
3. Call `LoadDataAsync()`, deserialize the anonymous result to JSON (as the existing sibling test does), and assert `data.formattedAmount` equals the expected string.

**Acceptance criteria:**
- All FR-1..FR-4 scenarios pass using this pattern, expressed as a single `[Theory]`/`[InlineData]` parameterized test (per the brief's suggested approach) plus one dedicated zero-amount case, OR as individual `[Fact]`s — either satisfies this spec; the architect/planner should pick one for consistency with `LowStockEfficiencyTileTests.cs` conventions.

## Non-Functional Requirements

### NFR-1: Coverage
Line coverage for `PurchaseOrdersInTransitTile.cs` must rise from 0.0% to at least the 60% filter threshold named in the brief. Covering `FormatAmountInThousands`'s three branches plus one `LoadDataAsync` happy-path call is expected to clear this threshold given the file's small size.

### NFR-2: No production code change required
This is a test-only gap-closing task. `FormatAmountInThousands`'s existing behavior is the specification under test — no behavior change is in scope unless a test reveals an actual bug (see Open Questions on rounding).

## Data Model
No data model changes. Tests construct in-memory `PurchaseOrder` domain entities (existing type, `backend/src/Anela.Heblo.Domain/Features/Purchase/PurchaseOrder.cs`) purely as repository-mock return values.

## API / Interface Design
No API changes. Test-only addition of `backend/test/Anela.Heblo.Tests/Features/Purchase/DashboardTiles/PurchaseOrdersInTransitTileTests.cs`.

## Dependencies
- xUnit, Moq, FluentAssertions (already used project-wide per `docs/architecture/testing-strategy.md` and the sibling `LowStockEfficiencyTileTests.cs`).
- `IPurchaseOrderRepository` (existing interface) and `PurchaseOrder`/`PurchaseOrderLine` (existing domain entities) — no new test doubles or fixtures needed.

## Out of Scope
- Any change to `PurchaseOrdersInTransitTile.cs` production code, unless a discovered rounding discrepancy is confirmed as a real bug (flagged as an open question, not assumed).
- Frontend `PurchaseOrdersInTransitTile.tsx` — the brief and coverage report concern only the backend file.
- Broader test coverage of `LoadDataAsync`'s other fields (`count`, `totalAmount`, `metadata`, `drillDown`) beyond what's needed to exercise `formattedAmount` — these may be asserted incidentally but are not the focus.

## Open Questions
None.
