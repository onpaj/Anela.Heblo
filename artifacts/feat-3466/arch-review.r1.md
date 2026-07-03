# Architecture Review: Remove `ProductMarginSegmentDto` Backward-Compatibility Aliases

## Skip Design: true

## Architectural Fit Assessment

This is a dead-code deletion inside an existing DTO — no new endpoint, no new module, no UI change. `ProductMarginSegmentDto` (`backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/ProductMarginSegmentDto.cs:20-26`) carries six computed properties under a `// Keep for backward compatibility` comment that alias the canonical fields already used everywhere:

- `ProductCode => GroupKey`, `ProductName => DisplayName`
- `MarginPerPiece => AverageMarginPerPiece`, `SellingPriceWithoutVat => AverageSellingPriceWithoutVat`
- `MaterialCosts => AverageMaterialCosts`, `LaborCosts => AverageLaborCosts`

Verified directly:
- **Producer**: `MonthlyBreakdownGenerator.cs:95-108` constructs `ProductMarginSegmentDto` using only canonical property names.
- **Backend consumers**: repo-wide search finds no `.ProductCode` / `.ProductName` / `.MarginPerPiece` / `.SellingPriceWithoutVat` / `.MaterialCosts` / `.LaborCosts` read on a `ProductMarginSegmentDto` instance anywhere in `backend/src` or `backend/test`. The only test reference, `GetProductMarginSummaryHandlerTests.cs:215`, builds an empty `List<ProductMarginSegmentDto>()`.
- **Frontend production consumer**: `ProductMarginSummary.tsx:245-250` reads only `segment.averageMarginPerPiece`, `segment.averageSellingPriceWithoutVat`, `segment.unitsSold`, `segment.productCount`, `segment.averageMaterialCosts`, `segment.averageLaborCosts`, and matches segments via `segment.displayName` — all canonical names.
- **Generated client**: `frontend/src/api/generated/api-client.ts:12920-13015` currently emits both the canonical fields and the six dead aliases (`productCode`, `productName`, `marginPerPiece`, `sellingPriceWithoutVat`, `materialCosts`, `laborCosts`) on both the `ProductMarginSegmentDto` class and `IProductMarginSegmentDto` interface — exactly the redundant public surface the spec targets.

This is a direct sibling of the already-completed `TopProductDto` shim removal (`docs/superpowers/plans/2026-06-10-remove-topproductdto-shims.md`), which explicitly deferred this DTO. The same mechanical pattern applies: delete the getters, rebuild, regenerate the NSwag client, run tests.

One thing the spec did not surface: `frontend/src/components/pages/__tests__/ProductMarginSummary.test.tsx:31-58` (the `productSegments` fixture inside `mockData`) still uses the **old alias names** (`productCode`, `productName`, `marginPerPiece`, `sellingPriceWithoutVat`, `materialCosts`, `laborCosts`) instead of canonical ones. See Decision 1 and the Specification Amendment below for why this doesn't block the change but should still be cleaned up.

## Proposed Architecture

No new architecture — this is a subtractive change to an existing contract. There is no component overview beyond "one DTO loses six properties, one generated file shrinks."

### Key Design Decisions

#### Decision 1: Whether to also fix the stale `productSegments` test fixture
**Options considered:**
- (a) Leave `ProductMarginSummary.test.tsx`'s `productSegments` fixture untouched, per the spec's stated scope (only `GetProductMarginSummaryHandlerTests.cs:215` was identified as a test reference).
- (b) Rename the fixture's six fields to canonical names (`groupKey`, `displayName`, `averageMarginPerPiece`, `averageSellingPriceWithoutVat`, `averageMaterialCosts`, `averageLaborCosts`), mirroring what the precedent plan did for the `topProducts` fixture in the same file.

**Chosen approach:** (b) — rename the fixture fields for consistency, as a small additive cleanup within this change.

**Rationale:** The fixture is passed via `mockUseProductMarginSummary.mockReturnValue({ data: mockData, ... } as any)` — the `as any` cast means TypeScript will not catch the mismatch, and because `react-chartjs-2`'s `Chart` is mocked to a `<div>` that only `JSON.stringify`s its props, the tooltip callback function (the only code that reads these fields) is never invoked in the test — functions are dropped by `JSON.stringify`. So leaving it alone will **not** break `npm run build`, `npm run lint`, or the Jest suite; FR-4's own acceptance grep (`\.productCode|\.productName|...`, a dot-access pattern) also would not flag it, since the fixture uses object-literal keys, not dot access. Nothing in this change is *blocked* by the stale fixture. But leaving it makes the fixture describe a shape (`productCode`, `marginPerPiece`, ...) that no longer exists on the real DTO, which will confuse the next person touching this test. The precedent plan already renamed the sibling `topProducts` fixture in this exact file for the same reason — doing the same here keeps the file internally consistent and costs one small, low-risk `Edit`.

## Implementation Guidance

### Directory / Module Structure
No structural change. Files touched:
- `backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/ProductMarginSegmentDto.cs` — delete lines 20-26 (comment + six getters).
- `frontend/src/api/generated/api-client.ts` — regenerated only, never hand-edited (`npm run generate-client` per `docs/development/api-client-generation.md`).
- `frontend/src/components/pages/__tests__/ProductMarginSummary.test.tsx` — rename six fields inside the `productSegments` fixture (lines 31-58) to their canonical equivalents (recommended cleanup, see Decision 1; not required for build/test to pass).

### Interfaces and Contracts
`ProductMarginSegmentDto` after the change (matches the spec's Data Model section exactly):
```csharp
public class ProductMarginSegmentDto
{
    public string GroupKey { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public decimal MarginContribution { get; set; }
    public decimal Percentage { get; set; }
    public string ColorCode { get; set; } = string.Empty;
    public bool IsOther { get; set; } = false;

    public decimal AverageMarginPerPiece { get; set; }
    public int UnitsSold { get; set; }
    public decimal AverageSellingPriceWithoutVat { get; set; }
    public decimal AverageMaterialCosts { get; set; }
    public decimal AverageLaborCosts { get; set; }
    public int ProductCount { get; set; }
}
```
No route, verb, or request contract changes. The wire payload for any response embedding `ProductMarginSegmentDto` drops six redundant JSON fields; no real consumer reads them (verified above).

### Data Flow
Unchanged: `MonthlyBreakdownGenerator.GenerateMonthlySegments` → `ProductMarginSegmentDto` → serialized into the product-margin-summary analytics response → `ProductMarginSummary.tsx` chart tooltip. Only the DTO's public surface shrinks; no step in the pipeline is reordered or altered.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| NSwag regeneration touches unrelated types/interfaces beyond this DTO | Low | Inspect `git diff frontend/src/api/generated/api-client.ts` after regeneration; confirm the diff is scoped to `ProductMarginSegmentDto` / `IProductMarginSegmentDto` only (same check the `TopProductDto` precedent plan performed). |
| A hidden consumer of the six aliases was missed by the repo-wide grep | Low | `dotnet build` and `dotnet test backend/Anela.Heblo.sln` will fail loudly on any missed backend reference (compile error); `npm run build` / `npm run lint` / Jest will fail on any missed frontend reference that isn't behind an `any` cast. |
| Stale test fixture (`ProductMarginSummary.test.tsx` `productSegments`) continues to reference field names that no longer exist on the DTO, misleading future maintainers | Low | Rename the six fixture fields to canonical names in the same commit (Decision 1). Not required for tests to pass, but keeps the file consistent with the DTO and with the sibling `topProducts` fixture already renamed in the precedent cleanup. |

## Specification Amendments

- Add `frontend/src/components/pages/__tests__/ProductMarginSummary.test.tsx` (the `productSegments` array inside `mockData`, lines 31-58) to FR-3/FR-4's file list. Rename `productCode`→`groupKey`, `productName`→`displayName`, `marginPerPiece`→`averageMarginPerPiece`, `sellingPriceWithoutVat`→`averageSellingPriceWithoutVat`, `materialCosts`→`averageMaterialCosts`, `laborCosts`→`averageLaborCosts` on both fixture entries. This does not change FR-4's pass/fail outcome (the fixture is `any`-cast and the reading code path is never invoked under the mocked `Chart` component) — it's a consistency cleanup, not a required fix.
- Note for the record: the precedent plan (`docs/superpowers/plans/2026-06-10-remove-topproductdto-shims.md`) attributes this same fixture block to a type called `MonthlyProductSegmentDto`, which does not exist anywhere in the current codebase (verified by repo-wide search). The fixture actually types against `ProductMarginSegmentDto` (per `productSegments?: ProductMarginSegmentDto[]` in the generated client). This appears to be a stale/inaccurate reference in that older plan document — worth a mental correction, no action needed since that plan is already complete.

## Prerequisites
None. No schema, migration, feature-flag, or cross-team dependency. Proceed directly to implementation following the FR-1 → FR-4 sequence in `spec.r1.md`, plus the fixture rename noted above.
