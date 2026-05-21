# Specification: Extract duplicated `GetConsumptionTypeText` helper in PackingMaterials module

## Summary
The private static method `GetConsumptionTypeText(ConsumptionType)` is copy-pasted verbatim across five handler files in the `PackingMaterials` module. Consolidate these five copies into a single internal static helper inside the module's `Contracts/` folder so that future enum additions only need to be edited in one place.

## Background
Each handler in `PackingMaterials/UseCases/*` formats `ConsumptionType` enum values into Czech UI strings (e.g. `PerOrder → "za zakázku"`). The same `switch` expression is duplicated five times. When a new `ConsumptionType` value is added, all five copies must be kept in sync; the existing fallback (`_ => type.ToString()`) silently emits the raw enum name when a copy is forgotten, masking the bug rather than surfacing it.

This is a mechanical, zero-risk refactor that removes a known drift hazard without changing observable behavior.

## Functional Requirements

### FR-1: Introduce a single shared helper
Create an `internal static` class `PackingMaterialsTextHelper` in `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/PackingMaterialsTextHelper.cs` exposing a single method `ConsumptionTypeText(ConsumptionType type)` that returns the Czech label string for each `ConsumptionType` value.

**Acceptance criteria:**
- File `PackingMaterialsTextHelper.cs` exists under `Application/Features/PackingMaterials/Contracts/`.
- Class is declared `internal static`.
- Method signature: `public static string ConsumptionTypeText(ConsumptionType type)`.
- Switch expression preserves the exact same mappings as the current implementations:
  - `ConsumptionType.PerOrder → "za zakázku"`
  - `ConsumptionType.PerProduct → "za produkt"`
  - `ConsumptionType.PerDay → "za den"`
  - default → `type.ToString()`
- No other public members are added.

### FR-2: Remove all five duplicated private methods
Delete the private static `GetConsumptionTypeText` method from each of the five handler files and replace every call site with `PackingMaterialsTextHelper.ConsumptionTypeText(...)`.

**Acceptance criteria:**
- The private method is removed from each of:
  - `CreatePackingMaterial/CreatePackingMaterialHandler.cs`
  - `UpdatePackingMaterial/UpdatePackingMaterialHandler.cs`
  - `UpdatePackingMaterialQuantity/UpdatePackingMaterialQuantityHandler.cs`
  - `GetPackingMaterialsList/GetPackingMaterialsListHandler.cs`
  - `GetPackingMaterialLogs/GetPackingMaterialLogsHandler.cs`
- Every former call site now invokes `PackingMaterialsTextHelper.ConsumptionTypeText(...)`.
- A repository-wide search for `GetConsumptionTypeText` returns zero results after the change.
- No other code in these handlers is touched (surgical change only).

### FR-3: Preserve behavior
The refactor must not change any externally observable behavior. API responses returning consumption-type labels must produce byte-identical strings before and after the change.

**Acceptance criteria:**
- Existing unit/integration tests for the five handlers continue to pass with no modification.
- No new enum values are introduced as part of this change.
- No call site is removed or relocated; only the method invoked is renamed/redirected.

## Non-Functional Requirements

### NFR-1: Performance
No performance impact. A static method call on a `switch` expression has identical runtime cost to the inline private method.

### NFR-2: Security
None — this change touches presentation-string formatting only. No new inputs, outputs, persistence, or trust boundaries.

### NFR-3: Maintainability
Single source of truth for `ConsumptionType` Czech labels eliminates drift between handlers. Future enum additions require updating exactly one switch arm.

### NFR-4: Build & validation
- `dotnet build` succeeds with zero warnings introduced by this change.
- `dotnet format` produces no diff after the change.
- All existing tests in `Anela.Heblo.Application.Tests` (or the relevant test project) pass.

## Data Model
No changes to the data model. The `ConsumptionType` enum itself is **not** modified.

## API / Interface Design
No external API changes. No new public types, no new endpoints, no DTO modifications. The new helper is `internal` to `Anela.Heblo.Application`.

Internal-only addition:
```csharp
// Application/Features/PackingMaterials/Contracts/PackingMaterialsTextHelper.cs
internal static class PackingMaterialsTextHelper
{
    public static string ConsumptionTypeText(ConsumptionType type) => type switch
    {
        ConsumptionType.PerOrder   => "za zakázku",
        ConsumptionType.PerProduct => "za produkt",
        ConsumptionType.PerDay     => "za den",
        _ => type.ToString()
    };
}
```

## Dependencies
None. The change is confined to the `PackingMaterials` feature folder in `Anela.Heblo.Application`. The `ConsumptionType` enum is already referenced by every affected handler.

## Out of Scope
- Localization framework / resource-file based translation of `ConsumptionType` labels (still hard-coded Czech strings).
- Removing the `_ => type.ToString()` fallback or replacing it with an exception.
- Adding unit tests for the new helper (existing handler tests already exercise the mappings indirectly).
- Touching any other duplicated helpers elsewhere in the codebase.
- Frontend changes — none required; the API contract is unchanged.
- Renaming, reordering, or modifying `ConsumptionType` enum values.

## Open Questions
None.

## Status: COMPLETE