# Specification: Remove Unused `Severe` Member from `StockSeverity` Enum

## Summary
The `StockSeverity` enum in the Purchase module declares a `Severe` member that is never assigned by the backend, never matched by frontend rendering helpers, and never used in filter logic, yet it leaks into the generated TypeScript API contract. This spec defines the removal of `Severe` from the enum and the resulting client regeneration so the public contract reflects only values the backend can actually return.

## Background
During the daily arch-review routine on 2026-05-27, a dead enum member was identified in the Purchase stock analysis feature.

- The backend file `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisResponse.cs` (line 98) defines `StockSeverity.Severe`.
- The producer `StockSeverityCalculator.DetermineStockSeverity` returns only `NotConfigured`, `Critical`, `Low`, `Overstocked`, or `Optimal`.
- The filter `ShouldIncludeItem` in `GetPurchaseStockAnalysisHandler` has no branch for `Severe` under `StockStatusFilter`.
- The frontend helpers `getSeverityColorClass` and `getSeverityDisplayText` in `usePurchaseStockAnalysis.ts` do not handle `Severe`; it falls through to the default branch.
- The OpenAPI-generated `api-client.ts` exposes `StockSeverity.Severe = "Severe"` to consumers despite being unreachable.

The unrelated `GiftPackageSeverity.Severe` in the Logistics module is out of scope.

A misleading public enum increases cognitive load, bloats the generated TypeScript type, and invites consumers to write branches that can never fire. Removing it makes the contract honest with no behavior change.

## Functional Requirements

### FR-1: Remove `Severe` from the backend `StockSeverity` enum
The `Severe` member must be deleted from `StockSeverity` in `GetPurchaseStockAnalysisResponse.cs`. No other enum members may be modified, renamed, or reordered.

**Acceptance criteria:**
- `StockSeverity` declares exactly: `NotConfigured`, `Critical`, `Low`, `Optimal`, `Overstocked` (existing members, minus `Severe`).
- A repository-wide search for `StockSeverity.Severe` returns zero references in the backend solution.
- `dotnet build` succeeds with no new warnings or errors.

### FR-2: Regenerate the TypeScript API client
After the backend change, the OpenAPI-generated TypeScript client must be regenerated so consumers no longer see `StockSeverity.Severe`.

**Acceptance criteria:**
- `frontend/src/api/generated/api-client.ts` (or equivalent generated file) no longer contains `Severe = "Severe"` under the `StockSeverity` enum.
- `npm run build` in `frontend/` succeeds.
- `npm run lint` in `frontend/` succeeds with no new warnings.

### FR-3: Verify no frontend regression
The frontend helpers `getSeverityColorClass` and `getSeverityDisplayText` in `usePurchaseStockAnalysis.ts` already lack a `Severe` branch, so no changes are required. The task must confirm no other frontend reference to `StockSeverity.Severe` exists.

**Acceptance criteria:**
- A repository-wide search for `Severe` within Purchase frontend code returns no live references to the removed enum value.
- TypeScript compilation in `frontend/` passes.

### FR-4: Preserve unrelated severity types
`GiftPackageSeverity` in the Logistics module must remain unchanged, including its own `Severe` member.

**Acceptance criteria:**
- `GiftPackageSeverity.Severe` is still present and untouched after the change.
- No file outside `backend/src/Anela.Heblo.Application/Features/Purchase/` and the regenerated frontend client is modified.

## Non-Functional Requirements

### NFR-1: Performance
No performance impact. This is a contract cleanup; runtime behavior is unchanged.

### NFR-2: Security
No security implications. The removed member is unreachable in current code, so removal cannot expose or hide any sensitive data path.

### NFR-3: Backward Compatibility
The Anela.Heblo project is a single-deployment workspace with a single first-party consumer (its own frontend). The backend never serialized `Severe`, so removing it from the contract does not break any existing serialized payloads, persisted data, or external integrations. No deprecation cycle is required.

### NFR-4: Test Coverage
Existing tests covering the Purchase stock analysis feature must continue to pass. No new tests are required because no behavior changes; however, if any test references `StockSeverity.Severe` (it should not, given it is never produced), that test must be updated to reflect the reduced enum.

**Acceptance criteria:**
- All Purchase-module backend tests pass.
- All frontend tests touching `usePurchaseStockAnalysis` pass.

## Data Model
No data model change. `StockSeverity` is a transport-layer enum used only in the `GetPurchaseStockAnalysis` response DTO; it is not persisted to the database. The post-change shape is:

```
StockSeverity:
  - NotConfigured
  - Critical
  - Low
  - Optimal
  - Overstocked
```

(Final member order matches the existing declaration with `Severe` removed in place.)

## API / Interface Design
The public API surface affected is the response of the `GetPurchaseStockAnalysis` use case. The OpenAPI schema for `StockSeverity` will drop the `"Severe"` string from its enum array. No endpoint paths, request shapes, or other response fields change.

## Dependencies
- .NET 8 backend build toolchain (`dotnet build`, `dotnet format`).
- Frontend OpenAPI client generation pipeline triggered by `npm run build` in `frontend/`.
- No external services, libraries, or feature flags involved.

## Out of Scope
- Any change to `GiftPackageSeverity` in the Logistics module.
- Refactoring of `StockSeverityCalculator` logic.
- Refactoring of `getSeverityColorClass` or `getSeverityDisplayText` beyond what naturally follows from regeneration (no source edits required).
- Adding new severity classifications or thresholds.
- Changes to `StockStatusFilter` or any other Purchase enum.
- Database migrations or seed data updates.

## Open Questions
None.

## Status: COMPLETE