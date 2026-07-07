# Specification: Remove `ProductMarginSegmentDto` Backward-Compatibility Alias Properties

## Summary
`ProductMarginSegmentDto` carries six computed properties (`ProductCode`, `ProductName`, `MarginPerPiece`, `SellingPriceWithoutVat`, `MaterialCosts`, `LaborCosts`) that are read-only aliases for the canonical properties (`GroupKey`, `DisplayName`, `AverageMarginPerPiece`, `AverageSellingPriceWithoutVat`, `AverageMaterialCosts`, `AverageLaborCosts`). No backend or frontend code reads them. This spec removes the six aliases and regenerates the downstream TypeScript client so the DTO exposes exactly one canonical name per concept.

## Background
`ProductMarginSegmentDto` (`backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/ProductMarginSegmentDto.cs`) is populated by `MonthlyBreakdownGenerator.GenerateMonthlySegments` and consumed by the frontend `ProductMarginSummary.tsx` chart tooltip, which already reads only the canonical `average*` / `groupKey` / `displayName` fields. The six properties under the `// Keep for backward compatibility` comment (lines 20–26) have no producer or consumer anywhere in the repo. Because the TypeScript client (`frontend/src/api/generated/api-client.ts`) is auto-generated from the OpenAPI spec, these dead aliases currently appear verbatim in the public API contract, creating confusion about which name is canonical and adding upkeep burden (they must be kept in sync with the primary properties whenever those change).

This mirrors an identical, already-completed cleanup on the sibling `TopProductDto` (see `docs/superpowers/plans/2026-06-10-remove-topproductdto-shims.md`), which explicitly deferred this DTO as a separate follow-up. This spec is that follow-up.

## Functional Requirements

### FR-1: Delete the six alias properties from `ProductMarginSegmentDto`
Remove the `// Keep for backward compatibility` comment and the six computed properties (`ProductCode`, `ProductName`, `MarginPerPiece`, `SellingPriceWithoutVat`, `MaterialCosts`, `LaborCosts`) from `backend/src/Anela.Heblo.Application/Features/Analytics/Contracts/ProductMarginSegmentDto.cs`. No other members of the class change.

**Acceptance criteria:**
- The file no longer declares any of the six named properties or the associated comment.
- The six canonical properties (`GroupKey`, `DisplayName`, `AverageMarginPerPiece`, `AverageSellingPriceWithoutVat`, `AverageMaterialCosts`, `AverageLaborCosts`) and all other existing members are unchanged.
- `dotnet build` succeeds with no new errors.

### FR-2: Confirm no backend call-site references the removed properties
A repo-wide scan confirms no backend production or test code reads `.ProductCode`, `.ProductName`, `.MarginPerPiece`, `.SellingPriceWithoutVat`, `.MaterialCosts`, or `.LaborCosts` on a `ProductMarginSegmentDto` instance. (Manual verification already performed for this spec: `MonthlyBreakdownGenerator.cs` only sets canonical fields; the one test reference, `GetProductMarginSummaryHandlerTests.cs:215`, constructs an empty `List<ProductMarginSegmentDto>()` and does not touch any property.)

**Acceptance criteria:**
- `dotnet build` and `dotnet test backend/Anela.Heblo.sln` succeed with no compile errors or new test failures after FR-1 is applied.

### FR-3: Regenerate the TypeScript client
Regenerate `frontend/src/api/generated/api-client.ts` (per `docs/development/api-client-generation.md`, `npm run generate-client` from `frontend/`) so `ProductMarginSegmentDto` / `IProductMarginSegmentDto` in the generated client no longer declare `productCode`, `productName`, `marginPerPiece`, `sellingPriceWithoutVat`, `materialCosts`, or `laborCosts`. Do not hand-edit the generated file.

**Acceptance criteria:**
- The regenerated `ProductMarginSegmentDto` class and `IProductMarginSegmentDto` interface no longer declare the six removed fields.
- The diff to `api-client.ts` is scoped to this DTO only — no other interfaces, classes, or controller method signatures change.
- `npm run build` succeeds with no TypeScript errors, and re-running the prebuild client generator produces a byte-identical file (idempotent regeneration).

### FR-4: No frontend consumer regression
`ProductMarginSummary.tsx` already reads only canonical field names (`averageMarginPerPiece`, `averageSellingPriceWithoutVat`, `averageMaterialCosts`, `averageLaborCosts`, confirmed at lines 245–250) and does not reference the removed aliases anywhere.

**Acceptance criteria:**
- `grep -nE "\.productCode|\.productName|\.marginPerPiece|\.sellingPriceWithoutVat|\.materialCosts|\.laborCosts"` against `ProductMarginSummary.tsx` (and any other file consuming `ProductMarginSegmentDto`/`IProductMarginSegmentDto` instances) returns no matches.
- `npm run lint`, `npm run build`, and the existing frontend test suite pass unchanged.

## Non-Functional Requirements

### NFR-1: Maintainability
The DTO exposes exactly one canonical name per concept, eliminating the risk that future changes to the primary properties silently desynchronize an unused alias.

### NFR-2: No behavior change
This is a pure surface-area reduction. No API endpoint, response shape (beyond dropping unused fields), business logic, or UI behavior changes for any real caller.

## Data Model
`ProductMarginSegmentDto` after the change:

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

No changes to any other entity, database schema, or migration are involved — this DTO is not persisted.

## API / Interface Design
No endpoint route, verb, or request contract changes. The response DTO for any endpoint that serializes `ProductMarginSegmentDto` (e.g., the monthly breakdown segment list returned by the product margin summary analytics endpoint) drops the six redundant JSON fields (`productCode`, `productName`, `marginPerPiece`, `sellingPriceWithoutVat`, `materialCosts`, `laborCosts`) from its payload. This is a backward-incompatible change to the wire format only for consumers relying on those specific field names — and analysis confirms there are none in this codebase.

## Dependencies
- NSwag-based OpenAPI client generation pipeline (`npm run generate-client`), per `docs/development/api-client-generation.md`.
- No external services or new libraries required.

## Out of Scope
- Any change to `MarginCalculator.cs`, `ReportBuilderService.cs`, `ProductFilterService.cs`, `GetProductMarginAnalysis*.cs`, `ProductMarginSummaryDto.cs`, `TopProductDto.cs` (already cleaned up separately), or any other DTO — these are separate types and not touched by this change.
- Any change to `MonthlyProductSegmentDto`, which legitimately owns its own `productCode` / `productName` fields for a different purpose (per the precedent plan for `TopProductDto`) and is unrelated to `ProductMarginSegmentDto`.
- Any sort-key string literal cleanup (e.g., `"productcode"` / `"productname"` arms in handler sorting logic) — out of scope unless a future scan finds such literals tied to this specific DTO.
- Any UI/UX change — the frontend already renders correctly using canonical fields today.

## Open Questions
None. The repo-wide scan performed while producing this spec confirms: (1) `MonthlyBreakdownGenerator.cs` populates only canonical fields when constructing `ProductMarginSegmentDto`; (2) the sole test reference (`GetProductMarginSummaryHandlerTests.cs:215`) instantiates an empty list and touches no property; (3) `ProductMarginSummary.tsx` reads only canonical `average*` field names. This is a low-risk, single-commit deletion with no consumer migration required, following the exact precedent already executed for `TopProductDto`.

## Status: COMPLETE
