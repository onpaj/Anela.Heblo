# Specification: Remove unused `OrderIds` field from `PrintPickingListResult`

## Summary
Remove the dead `OrderIds` property from `PrintPickingListResult` and clean up the corresponding unused test arrange step. The field is initialized but never written by producers nor read by consumers, creating a misleading API surface that risks silent logic errors if future code attempts to use it.

## Background
A daily architecture review on 2026-06-07 flagged `PrintPickingListResult.OrderIds` as dead code in the ExpeditionList feature's Logistics integration chain:

- **Definition:** `backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListResult.cs:7` — declared as `List<int>` initialized to an empty list.
- **Producer:** `ShoptetApiExpeditionListSource.CreatePickingList` (`backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs:229`) constructs the result with only `ExportedFiles` and `TotalCount`; never sets `OrderIds`.
- **Consumer:** `LogisticsExpeditionPickingAdapter.CreatePickingListAsync` (`backend/src/Anela.Heblo.Application/Features/Logistics/Infrastructure/LogisticsExpeditionPickingAdapter.cs:16-22`) maps only `ExportedFiles` and `TotalCount` into `ExpeditionPickingResult`; `OrderIds` is silently discarded, and `ExpeditionPickingResult` has no equivalent field.
- **Test:** `LogisticsExpeditionPickingAdapterTests.CreatePickingListAsync_TranslatesResultFields` (`backend/test/Anela.Heblo.Tests/Features/Logistics/Infrastructure/LogisticsExpeditionPickingAdapterTests.cs:60`) sets `OrderIds = new List<int> { 1, 2, 3 }` in arrange but never asserts on it.

The field violates YAGNI: it advertises a capability that does not exist. A future developer reading the result type may write code that consumes `OrderIds`, observe an empty list, and produce a silent logic error that is difficult to diagnose because no exception is thrown and no compiler warning is raised.

## Functional Requirements

### FR-1: Remove `OrderIds` from `PrintPickingListResult`
Delete the `OrderIds` property from `PrintPickingListResult.cs`. No other production code references this property, so no replacement or migration is required.

**Acceptance criteria:**
- `PrintPickingListResult.cs` no longer declares an `OrderIds` property.
- `grep -r "OrderIds" backend/src/Anela.Heblo.Application/Features/Logistics/` returns no matches against `PrintPickingListResult`.
- `dotnet build` succeeds across the solution.

### FR-2: Clean up unused test arrange step
Remove the `OrderIds = new List<int> { 1, 2, 3 }` initializer from the arrange section of `CreatePickingListAsync_TranslatesResultFields`. The test currently sets a field it never asserts; with FR-1 applied, the initializer would fail to compile.

**Acceptance criteria:**
- The `OrderIds` initializer is removed from `LogisticsExpeditionPickingAdapterTests.cs`.
- The test `CreatePickingListAsync_TranslatesResultFields` continues to assert that `ExportedFiles` and `TotalCount` are correctly translated.
- No other assertions or test cases are altered.

### FR-3: Verify no hidden consumers
Confirm via repository-wide search that no other production code, serialization contract, or external consumer reads `PrintPickingListResult.OrderIds` before deletion. Because `PrintPickingListResult` is an internal application-layer DTO (not part of the OpenAPI surface), no client regeneration is expected.

**Acceptance criteria:**
- Repository-wide search for `OrderIds` returns no remaining references tied to `PrintPickingListResult` after the change.
- Generated OpenAPI client (`frontend/src/api/`) is unchanged, confirming the field was not exposed externally.

## Non-Functional Requirements

### NFR-1: Behaviour preservation
No runtime behaviour change. Producers, consumers, and downstream `ExpeditionPickingResult` mapping are unaffected because `OrderIds` was never written or read in production.

### NFR-2: Surgical scope
Touch only the three identified files. Do not refactor adjacent code, rename symbols, reorder properties, or reformat unrelated lines.

### NFR-3: Test coverage maintained
Existing test coverage of `LogisticsExpeditionPickingAdapter.CreatePickingListAsync` for `ExportedFiles` and `TotalCount` must remain intact. The arrange-step cleanup must not weaken any assertion.

## Data Model
`PrintPickingListResult` (application-layer DTO) currently contains:
- `ExportedFiles` — collection of generated picking-list files (retained)
- `TotalCount` — number of orders included (retained)
- `OrderIds` — `List<int>`, initialized empty, never populated (**to be removed**)

`ExpeditionPickingResult` (downstream domain result) is unchanged; it has no `OrderIds` field and never did.

## API / Interface Design
No public API or external interface changes. `PrintPickingListResult` is internal to the application layer and is not serialized to any HTTP response, OpenAPI contract, MediatR contract exposed to clients, or persistence schema.

## Dependencies
None. The change is confined to one DTO file in `Anela.Heblo.Application` and one test file in `Anela.Heblo.Tests`. No NuGet packages, configuration, migrations, or external services are involved.

## Out of Scope
- Renaming, restructuring, or otherwise modifying `ExpeditionPickingResult` or any other type in the Logistics or Picking namespaces.
- Adding new fields, telemetry, or logging to the picking-list flow.
- Refactoring `ShoptetApiExpeditionListSource.CreatePickingList` or `LogisticsExpeditionPickingAdapter.CreatePickingListAsync` beyond what is necessary for the field removal (which is nothing — neither file references `OrderIds`).
- Frontend changes; the field is not exposed via the OpenAPI client.
- Broader dead-code sweeps in the Logistics module.

## Open Questions
None.

## Status: COMPLETE