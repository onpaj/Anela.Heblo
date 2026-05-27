# Specification: Remove Logistics → Purchase Module Coupling in GiftPackageDto

## Summary
The Logistics module's `GiftPackageDto` currently depends on the `StockSeverity` enum owned by the Purchase module, violating the architectural rule that modules must communicate only through their own contracts. This spec defines the work to introduce a Logistics-owned severity enum and refactor all references so the cross-module compile-time dependency is removed.

## Background
Per `docs/architecture/development_guidelines.md`, each Vertical Slice module owns its contracts and must not import types from another module's internal namespaces. Today:

- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Contracts/GiftPackageDto.cs` imports `Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseStockAnalysis` and exposes `StockSeverity Severity { get; set; }`.
- `StockSeverity` is defined inside Purchase's use-case response class at `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisResponse.cs:96`.
- `GiftPackageManufactureService` also imports and uses the Purchase enum (lines 343, 349, 352).
- A frontend tile component (`CriticalGiftPackagesTile`) consumes the severity through the generated TypeScript client and is therefore indirectly bound to the same shared type.

The Manufacture module already follows the correct pattern with its own `ManufacturingStockSeverity` enum — Logistics should mirror that approach.

If Purchase renames, restructures, or narrows the meaning of `StockSeverity`, Logistics breaks unnecessarily. Removing the coupling restores module independence and prevents future cross-module ripple effects.

## Functional Requirements

### FR-1: Introduce a Logistics-owned severity enum
A new enum representing gift-package stock severity must exist inside the Logistics module's contracts namespace.

**Acceptance criteria:**
- A new file is created at `backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/GiftPackageSeverity.cs`.
- The enum is declared as `namespace Anela.Heblo.Application.Features.Logistics.Contracts; public enum GiftPackageSeverity { Optimal, Severe, Critical }`.
- The member ordering and underlying integer values match Purchase's `StockSeverity` so that any existing persisted/serialized values remain numerically equivalent.

### FR-2: Update `GiftPackageDto` to use the new enum
The Logistics DTO must stop importing Purchase types.

**Acceptance criteria:**
- `GiftPackageDto.cs` no longer contains `using Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseStockAnalysis;`.
- The `Severity` property is typed as `GiftPackageSeverity` (from `Anela.Heblo.Application.Features.Logistics.Contracts`).
- `GiftPackageDto` remains a class (per project rule — DTOs are not records).

### FR-3: Update `GiftPackageManufactureService` to use the new enum
The Logistics application service must use the Logistics-owned enum end-to-end.

**Acceptance criteria:**
- All occurrences of `StockSeverity` inside `GiftPackageManufactureService` (currently lines 343, 349, 352) are replaced with `GiftPackageSeverity`.
- The Purchase `using` import is removed from this file.
- Any helper/mapping logic that previously produced `StockSeverity` now produces `GiftPackageSeverity`.

### FR-4: Update frontend consumers to use the regenerated client type
The frontend `CriticalGiftPackagesTile` (and any sibling components that read `GiftPackageDto.severity`) must compile and behave identically against the new enum exposed by the regenerated TypeScript client.

**Acceptance criteria:**
- After OpenAPI client regeneration, the frontend builds with no type errors.
- The component's branching on severity values yields the same UI states (Optimal / Severe / Critical) as before the change.
- No frontend code imports or references the old `StockSeverity` symbol.

### FR-5: Verify no other Logistics code depends on Purchase's `StockSeverity`
Eliminate any remaining cross-module reference originating from Logistics.

**Acceptance criteria:**
- A repo-wide search for `Features.Purchase.UseCases.GetPurchaseStockAnalysis` from inside `Features/Logistics/**` returns zero matches.
- A repo-wide search for `StockSeverity` from inside `Features/Logistics/**` returns zero matches.

### FR-6: Preserve Purchase's own `StockSeverity`
Purchase continues to own and use its enum unchanged.

**Acceptance criteria:**
- `StockSeverity` remains defined at its current location in `GetPurchaseStockAnalysisResponse.cs` for use by Purchase's own use cases.
- Purchase code is not modified by this change.

### FR-7: Tests reflect the new enum
Any existing unit/integration test that asserts on `GiftPackageDto.Severity` or related Logistics service behavior must reference `GiftPackageSeverity`.

**Acceptance criteria:**
- All affected tests compile and pass.
- Test enum comparisons use the new Logistics-owned type.
- If no tests exist for the relevant code paths today, none are required to be added by this refactor (see Out of Scope).

## Non-Functional Requirements

### NFR-1: Performance
No runtime performance impact is expected. The change is purely a type relocation; enum values map 1:1 and have identical underlying integer representation.

### NFR-2: Security
No security impact. No auth, input validation, or data sensitivity boundaries are altered.

### NFR-3: Backward compatibility (serialization)
Because the project rule states DTOs use class (not record) generation and the enum members and ordinal values are preserved, JSON serialization output is identical. Any in-flight client built against the previous generated client must continue to deserialize successfully after the OpenAPI client regeneration step.

### NFR-4: Architectural conformance
The Logistics module must have zero compile-time dependencies on the Purchase module after this change, as required by `docs/architecture/development_guidelines.md`.

### NFR-5: Build and lint cleanliness
`dotnet build` + `dotnet format` and `npm run build` + `npm run lint` must all pass with no new warnings introduced by the refactor.

## Data Model
No database schema changes. The only data-model change is the C# type identity of the `Severity` field on `GiftPackageDto`:

- Before: `Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseStockAnalysis.StockSeverity`
- After: `Anela.Heblo.Application.Features.Logistics.Contracts.GiftPackageSeverity`

Members and ordinals (`Optimal = 0`, `Severe = 1`, `Critical = 2`) are preserved.

## API / Interface Design

### Backend
- The HTTP endpoint(s) returning `GiftPackageDto` keep identical JSON shape; the `severity` field's numeric values are unchanged.
- The OpenAPI schema name for the severity enum will change (e.g., from `StockSeverity` to `GiftPackageSeverity`). Downstream generated TypeScript client will reflect this rename.

### Frontend
- Regenerate the TypeScript OpenAPI client as part of build.
- Update `CriticalGiftPackagesTile` (and any other consumer) to import `GiftPackageSeverity` from the regenerated client.
- No UI/UX changes; the rendered output for each severity level stays the same.

## Dependencies
- Project rule: OpenAPI TypeScript client is auto-generated on build (`docs/development/api-client-generation.md`).
- Project rule: DTOs are classes, never C# records (`docs/architecture/development_guidelines.md`).
- Existing Logistics feature `GiftPackageManufacture` and its tile component on the dashboard.
- No new external libraries or services.

## Out of Scope
- Any refactor of Purchase module code, including `StockSeverity` itself.
- Renaming or refactoring `ManufacturingStockSeverity` in the Manufacture module.
- Introducing a shared cross-module "severity" abstraction in a common namespace — explicitly rejected, as it would re-create the coupling problem.
- Adding new tests for previously untested code paths; only existing tests touched by the type change need updating.
- Database migrations (none required).
- Behavioral changes to gift-package severity classification thresholds or business logic.

## Open Questions
None.

## Status: COMPLETE