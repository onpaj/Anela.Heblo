# Plan: Remove unused SafeMarginCalculator and SalesCost dead code

## Summary
`SafeMarginCalculator` and `SalesCost` in the Product Costing & Margin Calculation module are dead code: both are defined and (in the calculator's case) DI-registered and unit-tested, but neither has any production caller. The authoritative margin pipeline already runs through `MarginCalculationService` / `MarginLevel.Create`. This task removes the duplicate to eliminate the "which implementation is authoritative?" ambiguity flagged by the arch-review finding.

## Context
Verified directly against the current codebase (not just the finding text):
- `grep -rn "SafeMarginCalculator" backend/` returns only: the DI registration (`CatalogModule.cs:102`), the class definition itself (`SafeMarginCalculator.cs`), and its dedicated test file (`SafeMarginCalculatorTests.cs`). No handler, controller, or other service resolves or injects it.
- `grep -rn "\bSalesCost\b" backend/` returns only the class definition itself (`SalesCost.cs`). It is not DI-registered and has zero references anywhere else — distinct from `ISalesCostProvider`/`SalesCostProvider`/`ISalesCostCache`, which *are* used and registered (`CatalogModule.cs:84,96`) and are out of scope here.
- `MarginCalculationService.cs:108-117` is confirmed as the real, in-use margin computation path via `MarginLevel.Create(...)`.

This confirms the finding's premise: no intended caller exists for either type. The "consolidate behind SafeMarginCalculator" alternative in the finding is rejected — `MarginCalculationService` implements multi-level margin (M0/M1A/M1B/M2) via `MarginLevel`, a materially different and richer model than `SafeMarginCalculator`'s single flat percentage; routing the real pipeline through the simpler dead type would be a regression, not a consolidation.

## Functional requirements

**FR-1: Remove `SafeMarginCalculator` and its `MarginCalculationResult` companion type**
- Delete `backend/src/Anela.Heblo.Application/Features/Catalog/Services/SafeMarginCalculator.cs`.
- Acceptance: file no longer exists; no compile errors anywhere in the solution referencing `SafeMarginCalculator` or `MarginCalculationResult`.

**FR-2: Remove the DI registration**
- Delete `services.AddTransient<SafeMarginCalculator>();` at `CatalogModule.cs:102`.
- Acceptance: line removed; `dotnet build` succeeds.

**FR-3: Remove the dedicated test file**
- Delete `backend/test/Anela.Heblo.Tests/Features/Catalog/SafeMarginCalculatorTests.cs`.
- Acceptance: file no longer exists; test project builds and full test run passes with no orphaned references.

**FR-4: Remove `SalesCost` DTO**
- Delete `backend/src/Anela.Heblo.Application/Features/Catalog/Services/SalesCost.cs`.
- Acceptance: file no longer exists; confirm (re-grep) it is not referenced by `SalesCostProvider`, `SalesCostCache`, or any DTO/mapping code before deleting — it is a distinct, same-named-family but unrelated type from those, per the pre-check above.

**FR-5: No behavior change to the production margin pipeline**
- `MarginCalculationService` / `MarginLevel.Create` must be untouched.
- Acceptance: existing `MarginCalculationService` tests (and any margin-related integration tests) pass unchanged.

## Non-functional requirements
- Pure deletion — no new abstractions, no refactor of `MarginCalculationService` to "absorb" the removed logic (its multi-level model already supersedes the flat calculation `SafeMarginCalculator` performed).
- Keep the change minimal and reviewable as a single small diff (surgical-changes rule).

## Data model
No entities change. `MarginCalculationResult` (a plain result-wrapper class local to the deleted file) and `SalesCost` (a plain DTO) are removed outright; neither is part of any persisted or API-facing contract, so no OpenAPI/client regeneration is needed.

## Interfaces
None affected — nothing was ever wired into a controller, MediatR handler, or external contract.

## Dependencies and scope
**In scope:**
- `SafeMarginCalculator.cs` (incl. `MarginCalculationResult`), its DI line, its test file.
- `SalesCost.cs`.

**Explicitly out of scope:**
- `ISalesCostProvider` / `SalesCostProvider` / `ISalesCostCache` / `SalesCostCache` — actively used, unrelated to this cleanup despite the similar name.
- `MarginCalculationService` and `MarginLevel` — the authoritative implementation; not modified.
- Any other arch-review findings from this batch (Logistics, Photobank, AiAdapters, etc.) — separate tasks.

## Rough plan
1. Delete the three files: `SafeMarginCalculator.cs`, `SafeMarginCalculatorTests.cs`, `SalesCost.cs`.
2. Remove the `services.AddTransient<SafeMarginCalculator>();` line from `CatalogModule.cs`.
3. Build (`dotnet build`) to confirm no dangling references.
4. Run the full backend test suite to confirm nothing else depended on these types and no test collection breaks.
5. Run `dotnet format` per repo validation rules.

## Open questions
- None — the finding's own suggested "consolidate" alternative was evaluated and rejected because `MarginCalculationService`'s multi-level (M0/M1A/M1B/M2) model is strictly more capable than `SafeMarginCalculator`'s flat calculation; removal is the correct direction, not consolidation. If a future requirement needs the specific "safe division with validation" behavior, it can be reintroduced then — YAGNI applies now since it has zero current callers.
