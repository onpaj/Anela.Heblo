# Design: Remove duplicate margin-total calculation in GetProductMarginSummaryHandler

## Scope note

No UI is involved — this is a private-method-level refactor inside a single backend handler with no change to
any request/response contract, controller signature, or frontend consumer. The UX/UI section is omitted
accordingly. Component design is limited to the one affected class; there are no new components, modules, or
boundaries to introduce.

## Component design

**Affected component**: `GetProductMarginSummaryHandler` (`backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs`)

Current responsibility split (unchanged by this design):

- `MarginCalculator.CalculateAsync` — single source of truth for per-group aggregation. For each product surviving
  the `MarginAmount > 0` filter, computes `totalSold * GetMarginAmountForLevel(product, marginLevel)` and
  accumulates it into `MarginCalculationResult.GroupTotals[groupKey]`, alongside populating
  `GroupProducts[groupKey]` with the same filtered product list.
- `GetProductMarginSummaryHandler.GenerateTopProducts` — consumes `MarginCalculationResult` to build
  `TopProductDto` rows (one per group): display name, aggregated M0-M2 data, sort, rank.

The defect is that `GenerateTopProducts` does not trust `GroupTotals[groupKey]` as the total for
`TopProductDto.TotalMargin`. Instead it re-derives the same figure from `GroupProducts[groupKey]` via a private
helper, `CalculateTotalMarginForLevel`, which duplicates `MarginCalculator`'s summation logic verbatim. The two
computations are provably equivalent (same product list, same `marginLevel`, same formula), so the helper method
is redundant and its call site should be replaced with a direct read of the already-computed value.

**Interface change (internal only)**:

```
- private decimal CalculateTotalMarginForLevel(List<AnalyticsProduct> products, MarginLevel marginLevel)
```
is deleted entirely — it has no other callers (confirm via `grep -rn CalculateTotalMarginForLevel backend/`).

The single call site in `GenerateTopProducts`:

```csharp
// Before
var totalMarginForLevel = CalculateTotalMarginForLevel(products, marginLevel);
```

becomes:

```csharp
// After
var totalMarginForLevel = kvp.Value;
```

`kvp` is already in scope as the loop variable over `calculationResult.GroupTotals` (line 74-75 of the current
file), and `kvp.Value` is exactly `GroupTotals[kvp.Key]` — the pre-computed total for that group at
`request.MarginLevel`. No new parameters, fields, or dependencies are introduced; `_marginCalculator` remains used
elsewhere in the method (`GetGroupDisplayName`, `GetGroupAggregatedMarginData`) so the field itself is not
orphaned.

No other component's responsibilities change:

- `MarginCalculator` is untouched — it already does the correct, singular computation.
- `GetGroupAggregatedMarginData` (M0/M1/M2 amounts, percentages, prices) is a distinct, non-duplicate weighted
  average calculation and is out of scope.
- `_topProductSorter.Sort` and rank assignment operate on the resulting `TopProductDto` list unchanged.

## Data schemas

No schema changes of any kind:

- **Request/response DTOs**: `GetProductMarginSummaryRequest`, `GetProductMarginSummaryResponse`, `TopProductDto`
  are byte-for-byte unchanged in shape. `TopProductDto.TotalMargin` keeps its existing type (`decimal`) and
  meaning (total margin for the group at the selected `MarginLevel`) — only the code path producing its value
  changes, not its semantics or value for any given input.
- **Internal model**: `MarginCalculationResult.GroupTotals` (`Dictionary<string, decimal>`) is already populated
  today by `MarginCalculator.CalculateAsync`; this design simply makes `GenerateTopProducts` read that existing
  field instead of recomputing an identical value. No new fields are added to `MarginCalculationResult`.
- **Events / persistence / API contracts**: none involved. This handler has no event payloads and touches no
  database schema.

## Non-functional outcome

Eliminates one full redundant iteration over every product's `SalesHistory` per `GetProductMarginSummary`
request (previously two passes over the group's products for the margin total — one in `MarginCalculator`, one in
`CalculateTotalMarginForLevel` — now one). Response payload values are required to be identical before and after
for all inputs; the existing test suite
(`backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs`) already asserts on
`TotalMargin` for multiple scenarios and serves as the regression check — no new tests are designed here, per the
plan's determination that current coverage is sufficient.
