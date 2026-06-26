# Specification: Relocate UpdatePurchaseOrderRequestValidator to Correct Use Case Folder

## Summary
The `UpdatePurchaseOrderRequestValidator` is currently misplaced inside the `CreatePurchaseOrder` use case folder with an incorrect namespace. This spec covers moving the file to its proper `UpdatePurchaseOrder` use case folder and updating its namespace to align with Vertical Slice Architecture co-location principles. No behavior changes are introduced.

## Background
The codebase follows Vertical Slice Architecture, where each use case folder under `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/` contains all artifacts for that use case (handler, request/response DTOs, validators, mappings).

The file `UpdatePurchaseOrderRequestValidator.cs` validates `UpdatePurchaseOrderRequest` and `UpdatePurchaseOrderLineRequest`, both owned by the `UpdatePurchaseOrder` use case. However, it is physically located under `UseCases/CreatePurchaseOrder/` with namespace `Anela.Heblo.Application.Features.Purchase.UseCases.CreatePurchaseOrder`. This causes:
- IDE navigation from `UpdatePurchaseOrderHandler` to its validator to fail or mislead.
- Developers expecting to find the validator in `UseCases/UpdatePurchaseOrder/` will not find it there.
- Namespace ownership incorrectly attributes the validator to the `CreatePurchaseOrder` slice.

This was filed by the daily arch-review routine on 2026-05-27.

## Functional Requirements

### FR-1: Relocate validator file
Move `UpdatePurchaseOrderRequestValidator.cs` from the `CreatePurchaseOrder` use case folder to the `UpdatePurchaseOrder` use case folder.

**Acceptance criteria:**
- File no longer exists at `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/CreatePurchaseOrder/UpdatePurchaseOrderRequestValidator.cs`.
- File exists at `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrder/UpdatePurchaseOrderRequestValidator.cs`.
- File content (validation logic) is otherwise unchanged.

### FR-2: Update namespace declaration
Update the namespace inside the moved file to reflect its new location.

**Acceptance criteria:**
- Namespace line reads: `namespace Anela.Heblo.Application.Features.Purchase.UseCases.UpdatePurchaseOrder;`
- The `using Anela.Heblo.Application.Features.Purchase.UseCases.UpdatePurchaseOrder;` line is removed (no longer needed once namespaces align).
- No other `using` directives are removed unless they become genuinely unused.

### FR-3: Preserve validator registration and behavior
The validator must continue to be discovered and applied to `UpdatePurchaseOrderRequest` exactly as before.

**Acceptance criteria:**
- FluentValidation auto-registration (via `AddValidatorsFromAssembly` or equivalent in DI setup) continues to register the validator after the move.
- `UpdatePurchaseOrder` handler/pipeline invokes the validator with no code changes elsewhere.
- All existing tests pass without modification.

### FR-4: No regressions in build or callers
The move must not break compilation or any consumers.

**Acceptance criteria:**
- `dotnet build` succeeds with zero errors and no new warnings.
- `dotnet format` reports no violations on the moved file.
- No other source file requires changes (apart from imports/usings only if a consumer explicitly referenced the old namespace — none expected).

## Non-Functional Requirements

### NFR-1: Performance
No runtime performance impact expected. This is a purely structural change.

### NFR-2: Security
No security impact. No auth, validation rules, or data handling logic is modified.

### NFR-3: Maintainability
The change improves codebase maintainability by restoring Vertical Slice co-location, enabling correct IDE navigation, and aligning physical structure with logical ownership.

### NFR-4: Backwards Compatibility
No public API, contract, or behavior changes. No DB or migration impact. No frontend impact.

## Data Model
No data model changes.

## API / Interface Design
No API or interface changes. The validator is internal to the application layer.

## Dependencies
- FluentValidation (existing dependency) — assembly scanning must pick up validators in the `UpdatePurchaseOrder` folder; this is already the case for other validators in sibling use case folders.
- No new packages or external services.

## Out of Scope
- Refactoring or improving the validation rules themselves.
- Auditing other use case folders for similar misplacements (a separate arch-review task).
- Renaming the validator class or related DTOs.
- Changes to `CreatePurchaseOrder` use case logic or its own validators.
- Test additions beyond verifying that existing tests still pass.

## Open Questions
None.

## Status: COMPLETE