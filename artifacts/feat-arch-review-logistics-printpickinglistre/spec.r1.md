# Specification: Move PrintPickingList DTOs and IPickingListSource from Domain to Application Layer

## Summary
Three types currently sitting in `backend/src/Anela.Heblo.Domain/Features/Logistics/Picking/` — `PrintPickingListRequest`, `PrintPickingListResult`, and `IPickingListSource` — are operation-level DTOs and a use-case port, not domain primitives. Relocate them to `backend/src/Anela.Heblo.Application/Features/Logistics/Picking/` and update all `using`/namespace references across the application, adapters, and tests. No behavioural change.

## Background
The codebase follows Clean Architecture with a strict dependency rule: Domain is the innermost, most stable layer and must not depend on Application or Infrastructure concerns. The current `PrintPickingListRequest` carries application-layer concerns — `SendToPrinter` (I/O), `ChangeOrderState` (workflow side-effect), and `DefaultCarriers` (application configuration). `PrintPickingListResult` returns `ExportedFiles` (filesystem paths) and `OrderIds` — output of an Application use case, not a domain value object. `IPickingListSource` is a use-case port consumed by `ExpeditionListService` and implemented by `ShoptetApiExpeditionListSource` (Adapter); its contract is dictated by the Application layer, not the domain.

Keeping these types in Domain inverts the dependency rule and means any future change to the printing/queueing mechanism would ripple into Domain — the layer that should change least often. This finding was raised by the daily architecture-review routine on 2026-05-28 (`brief.md`).

## Functional Requirements

### FR-1: Relocate `PrintPickingListRequest`
Move `backend/src/Anela.Heblo.Domain/Features/Logistics/Picking/PrintPickingListRequest.cs` to `backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListRequest.cs`. Change the namespace from `Anela.Heblo.Domain.Features.Logistics.Picking` to `Anela.Heblo.Application.Features.Logistics.Picking`. Preserve the file contents otherwise: the class definition, all constants (`DefaultSourceStateId`, `DefaultDesiredStateId`), all properties (`Carriers`, `SourceStateId`, `DesiredStateId`, `ChangeOrderState`, `SendToPrinter`), and the static `DefaultCarriers` collection.

**Acceptance criteria:**
- File exists at the new path with the new namespace.
- File no longer exists at the old path.
- `PrintPickingListRequest` continues to reference `Carriers` enum from `Anela.Heblo.Domain.Features.Logistics` (an unmoved domain type) via a `using` directive or fully-qualified type name.
- `dotnet build` succeeds.

### FR-2: Relocate `PrintPickingListResult`
Move `backend/src/Anela.Heblo.Domain/Features/Logistics/Picking/PrintPickingListResult.cs` to `backend/src/Anela.Heblo.Application/Features/Logistics/Picking/PrintPickingListResult.cs`. Change the namespace from `Anela.Heblo.Domain.Features.Logistics.Picking` to `Anela.Heblo.Application.Features.Logistics.Picking`. Preserve all properties (`ExportedFiles`, `TotalCount`, `OrderIds`) and their defaults.

**Acceptance criteria:**
- File exists at the new path with the new namespace.
- File no longer exists at the old path.
- `dotnet build` succeeds.

### FR-3: Relocate `IPickingListSource`
Move `backend/src/Anela.Heblo.Domain/Features/Logistics/Picking/IPickingListSource.cs` to `backend/src/Anela.Heblo.Application/Features/Logistics/Picking/IPickingListSource.cs`. Change the namespace from `Anela.Heblo.Domain.Features.Logistics.Picking` to `Anela.Heblo.Application.Features.Logistics.Picking`. Preserve the `CreatePickingList` method signature exactly.

**Acceptance criteria:**
- File exists at the new path with the new namespace.
- File no longer exists at the old path.
- Implementing class `ShoptetApiExpeditionListSource` (Adapter) still compiles and satisfies the interface.

### FR-4: Update `using` directives in production code
The following production-code files currently import `Anela.Heblo.Domain.Features.Logistics.Picking` solely or partly to access the three moved types. Update each `using` directive to `Anela.Heblo.Application.Features.Logistics.Picking`. If a file still needs other types from the original namespace (none expected — verify by inspection), keep that `using` alongside the new one.

Files to update:
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/ExpeditionListService.cs`
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/IExpeditionListService.cs`
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/UseCases/RunExpeditionListPrintFix/RunExpeditionListPrintFixHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Infrastructure/Jobs/PrintPickingListJob.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/ShoptetApiAdapterServiceCollectionExtensions.cs`
- `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Expedition/ShoptetApiExpeditionListSource.cs`

**Acceptance criteria:**
- Each listed file imports `Anela.Heblo.Application.Features.Logistics.Picking` instead of (or in addition to, if other Domain.Logistics.Picking types remain) the Domain namespace.
- No production file references `Anela.Heblo.Domain.Features.Logistics.Picking` for the three moved types.
- `dotnet build` succeeds.

### FR-5: Update `using` directives in test projects
The following test files import the old Domain namespace. Update each to the new Application namespace.

Files to update:
- `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/ExpeditionListServiceOrderStateTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/ExpeditionListServicePrintSinkTests.cs`
- `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShoptetApiExpeditionListSourceTests.cs`
- `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Integration/PickingListIntegrationTests.cs`
- `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Expedition/ShoptetApiExpeditionListSource_CoolingMarkerTests.cs`

**Acceptance criteria:**
- Each listed test file imports the new namespace.
- All previously-passing tests in `Anela.Heblo.Tests` and `Anela.Heblo.Adapters.Shoptet.Tests` continue to pass with no behavioural changes.

### FR-6: Confirm `Picking/` directory cleanup in Domain
After FR-1, FR-2, FR-3, the `backend/src/Anela.Heblo.Domain/Features/Logistics/Picking/` directory should be empty. Remove the empty directory so the Domain layout cleanly reflects that picking is now an Application concern. If the directory contains files not enumerated in the brief (none identified during analysis), stop and report rather than deleting silently.

**Acceptance criteria:**
- Directory `backend/src/Anela.Heblo.Domain/Features/Logistics/Picking/` no longer exists.
- Domain project still builds cleanly.

### FR-7: Verify no remaining Domain → Application leakage for these types
Run a repository-wide search to confirm no source file still references `Anela.Heblo.Domain.Features.Logistics.Picking` for the three moved types after the migration. (Documentation under `docs/superpowers/specs/` and `docs/superpowers/plans/` may legitimately contain historical references — these are out of scope for code edits but should be noted.)

**Acceptance criteria:**
- `grep -r "Anela\.Heblo\.Domain\.Features\.Logistics\.Picking"` over `backend/src` and `backend/test` returns zero matches.
- Any matches under `docs/` are pre-existing historical references in spec/plan documents and are left untouched.

## Non-Functional Requirements

### NFR-1: Performance
No runtime performance impact expected. This is a compile-time refactor; resulting IL is equivalent.

### NFR-2: Security
No security surface change. No authentication, authorization, or data handling logic is altered.

### NFR-3: Backwards compatibility
- No public HTTP API contract changes — these DTOs are internal types and are not exposed by any controller endpoint or DTO contract surfaced by the OpenAPI generator.
- No persisted data (database schema, queue messages, blob serialization) references these types by namespace-qualified name. If any serialized payload exists that includes the full type name (e.g. Hangfire job arguments serialized via Newtonsoft with `TypeNameHandling.All` or similar), it must be flagged in Open Questions.

### NFR-4: Code style and conventions
- Follow the project's existing namespace-per-folder convention (already enforced in the target directory).
- Match the existing file style — no reformatting of unchanged lines, no adding/removing braces beyond what the namespace change requires (`namespace X;` file-scoped form should be preserved as-is).
- Run `dotnet format` on the modified files before commit.

## Data Model

No data model change. The moved types are DTOs used in-process; no entities, value objects, or database tables are affected. The `Carriers` enum, which `PrintPickingListRequest` references, stays in `Anela.Heblo.Domain.Features.Logistics` as a true domain primitive.

## API / Interface Design

No external API change. The internal C# interface `IPickingListSource` keeps its signature exactly; only its containing namespace changes.

Type movement summary:

| Type | Old location | New location |
|---|---|---|
| `PrintPickingListRequest` | `Domain/Features/Logistics/Picking/` | `Application/Features/Logistics/Picking/` |
| `PrintPickingListResult` | `Domain/Features/Logistics/Picking/` | `Application/Features/Logistics/Picking/` |
| `IPickingListSource` | `Domain/Features/Logistics/Picking/` | `Application/Features/Logistics/Picking/` |

## Dependencies

- The `Anela.Heblo.Application` project must already reference (transitively or directly) the `Anela.Heblo.Domain` project so that the moved `PrintPickingListRequest` can continue to use the `Carriers` enum. This dependency already exists in the current architecture.
- The `Anela.Heblo.Adapters.ShoptetApi` project must reference `Anela.Heblo.Application` (it already does, because it implements other Application-layer ports). Confirm during implementation that `ShoptetApiExpeditionListSource` and `ShoptetApiAdapterServiceCollectionExtensions` resolve the new namespace cleanly.
- The test projects (`Anela.Heblo.Tests`, `Anela.Heblo.Adapters.Shoptet.Tests`) already reference both Application and Domain projects; no `.csproj` changes are expected.

## Out of Scope

- Renaming any of the three types or altering their members, defaults, or semantics.
- Splitting `PrintPickingListRequest` into Command + Configuration types (a possible future improvement — out of scope here to keep the refactor surgical).
- Updating historical references in `docs/superpowers/specs/` or `docs/superpowers/plans/` — these are time-stamped artefacts of past work.
- Reorganizing other types currently in `Anela.Heblo.Domain.Features.Logistics/` (`Carriers`, `Warehouses`, `DeliveryHandling`, etc.) — they are out of this finding's scope.
- Modifying `ICarrierCoolingRepository`, `IShippingMethodCatalog`, or other Domain ports — they were not flagged.
- Adding new tests. Existing tests must continue to pass; no new coverage is required for a pure relocation.
- Any frontend or OpenAPI-client regeneration — these types are not part of any controller's request/response surface.

## Open Questions

None.

## Status: COMPLETE