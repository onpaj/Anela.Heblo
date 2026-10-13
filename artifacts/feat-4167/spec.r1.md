# Specification: Relocate Department and IDepartmentClient to the Analytics Domain Module

## Summary
`Department` and `IDepartmentClient` currently live in `Anela.Heblo.Domain.Features.InvoiceClassification`, but InvoiceClassification never consumes either type — it only stores department as a plain `string?` field. The real consumers (`FlexiDepartmentClient`, `FlexiDepartmentQueryService`, and their DI registration) belong to the Flexi Accounting adapter area and conceptually to the Analytics domain. This change moves both types to `Anela.Heblo.Domain.Features.Analytics`, updates their namespace, and fixes every consumer's `using` statements so the domain module boundary matches actual usage. No behavior changes.

## Background
This is an arch-review finding (GitHub issue #4167). The development guidelines (`docs/architecture/development_guidelines.md`) state each module owns only the contract interfaces and DTOs it actually consumes, and that cross-module coupling to a module's internal types should not happen. `IDepartmentClient`/`Department` sitting in `InvoiceClassification` violates this: it misleads readers into thinking InvoiceClassification manages departments, and needlessly couples the Flexi adapter layer to InvoiceClassification's namespace.

**Analyst verification against the current codebase** (this corrects two inaccuracies in the original issue body):

- Confirmed zero consumers of `Anela.Heblo.Domain.Features.InvoiceClassification.{Department, IDepartmentClient}` inside the InvoiceClassification module itself (`ClassificationRule.Department` / `ClassificationHistory.Department` are plain `string?` fields, unrelated to the `Department` class).
- Confirmed real consumers of the two Domain types are:
  1. `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/Departments/FlexiDepartmentClient.cs` — implements `Domain.Features.InvoiceClassification.IDepartmentClient`, returns `Department`.
  2. `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/Departments/FlexiDepartmentQueryService.cs` — consumes `IDepartmentClient`/`Department` via `using Anela.Heblo.Domain.Features.InvoiceClassification;`.
  3. `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Accounting/Departments/FlexiDepartmentQueryServiceTests.cs` — mocks `IDepartmentClient` via the same `using`.
  4. `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/FlexiAdapterServiceCollectionExtensions.cs` — DI registration (`services.AddScoped<IDepartmentClient, FlexiDepartmentClient>();`) via the same `using` at line 30. **The original issue's "three consumers" list omitted this DI registration file — it also needs its `using` updated.**
  5. **Correction:** the issue lists `Adapters.Flexi.Analytics.DepartmentSyncService` as a consumer. It is not. `DepartmentSyncService.cs` and its test have no `using Anela.Heblo.Domain.Features.InvoiceClassification;` at all — their `IDepartmentClient` is `Rem.FlexiBeeSDK.Client.Clients.Accounting.Departments.IDepartmentClient` (a third-party SDK type) and their `Department` is `Anela.Heblo.Persistence.Analytics.Entities.Department` (an unrelated EF persistence entity that happens to share the class name). Neither type is touched by this change, and `DepartmentSyncService.cs` requires **no edits**.
- Confirmed no naming collision: `Anela.Heblo.Domain.Features.Analytics` (the target namespace, an existing folder) does not currently define any `Department` type, so the move introduces no ambiguity. The `Persistence.Analytics.Entities.Department` EF entity used by `DepartmentSyncService` lives in a different namespace/assembly and is unaffected.
- `IDepartmentQueryService` (consumed by `FlexiDepartmentQueryService`) lives in `Anela.Heblo.Application/Features/UserManagement/Services/IDepartmentQueryService.cs` — out of scope for this change, left untouched.

## Functional Requirements

### FR-1: Move `Department` to the Analytics domain module
Move `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/Department.cs` to `backend/src/Anela.Heblo.Domain/Features/Analytics/Department.cs`, updating its namespace declaration from `Anela.Heblo.Domain.Features.InvoiceClassification` to `Anela.Heblo.Domain.Features.Analytics`. File content (the `Id`/`Name` properties) is otherwise unchanged.

**Acceptance criteria:**
- `Department.cs` no longer exists under `Features/InvoiceClassification/`.
- `Department.cs` exists under `Features/Analytics/` with namespace `Anela.Heblo.Domain.Features.Analytics`.
- Class body (properties, defaults) is byte-for-byte unchanged aside from the namespace line.

### FR-2: Move `IDepartmentClient` to the Analytics domain module
Move `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/IDepartmentClient.cs` to `backend/src/Anela.Heblo.Domain/Features/Analytics/IDepartmentClient.cs`, updating its namespace declaration the same way. The interface continues to reference `Department` (now in the same namespace, so no `using` needed within this file).

**Acceptance criteria:**
- `IDepartmentClient.cs` no longer exists under `Features/InvoiceClassification/`.
- `IDepartmentClient.cs` exists under `Features/Analytics/` with namespace `Anela.Heblo.Domain.Features.Analytics`.
- Method signatures (`GetDepartmentsAsync`, `GetDepartmentByIdAsync`) are unchanged.

### FR-3: Update all consumers' `using` statements
Update every file that references the moved types via `using Anela.Heblo.Domain.Features.InvoiceClassification;` (or a fully qualified `Domain.Features.InvoiceClassification.IDepartmentClient` reference) so it compiles against the new `Anela.Heblo.Domain.Features.Analytics` namespace instead. Exactly four files need this change:
1. `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/Departments/FlexiDepartmentClient.cs`
2. `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/Departments/FlexiDepartmentQueryService.cs`
3. `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/FlexiAdapterServiceCollectionExtensions.cs`
4. `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Accounting/Departments/FlexiDepartmentQueryServiceTests.cs`

`FlexiDepartmentClient.cs` also has a local type alias (`using IDepartmentClient = Rem.FlexiBeeSDK.Client.Clients.Accounting.Departments.IDepartmentClient;`) and disambiguates its base interface with a fully qualified `Domain.Features.InvoiceClassification.IDepartmentClient` — that qualification must become `Domain.Features.Analytics.IDepartmentClient` (or an equivalent updated `using`) to keep both `IDepartmentClient` names distinguishable in that file.

**Acceptance criteria:**
- No source or test file in the repository references `Anela.Heblo.Domain.Features.InvoiceClassification.Department` or `...IDepartmentClient` (verified by search after the change).
- `DepartmentSyncService.cs` and `DepartmentSyncServiceTests.cs` are **not** modified — they never referenced the Domain types being moved.
- No InvoiceClassification module file requires any change (confirmed zero real consumers there).

### FR-4: No behavior change
This is a pure rename/relocation refactor. No public API surface, DI wiring behavior, caching behavior, or runtime logic changes.

**Acceptance criteria:**
- `FlexiAdapterServiceCollectionExtensions.cs` still registers `services.AddScoped<IDepartmentClient, FlexiDepartmentClient>();` and `services.AddScoped<IDepartmentQueryService, FlexiDepartmentQueryService>();` with identical semantics, only pointing at the new namespace.
- All existing tests pass unmodified in behavior (only `using`/namespace references change, no test logic or assertions change).

## Non-Functional Requirements

### NFR-1: Build integrity
The solution must build cleanly (`dotnet build`) with zero new warnings or errors introduced by the namespace change.

### NFR-2: Test integrity
All existing backend tests (`Anela.Heblo.Tests`, `Anela.Heblo.Adapters.Flexi.Tests`) must continue to pass without modification to test assertions or behavior — only `using` directives change where needed.

## Data Model
No data model changes. `Department` remains a plain class with `Id` (string) and `Name` (string) properties; `IDepartmentClient` remains a two-method interface. Only the namespace/file location changes.

## API / Interface Design
No public HTTP API, MediatR contract, or frontend-facing change. This is an internal domain-module reorganization confined to backend C# namespaces.

## Dependencies
None beyond the existing Flexi adapter project referencing `Anela.Heblo.Domain`.

## Out of Scope
- Renaming or restructuring `IDepartmentQueryService` (`Application/Features/UserManagement/Services/`) — unrelated interface, not part of this finding.
- Any change to `DepartmentSyncService.cs`, `Persistence.Analytics.Entities.Department`, or `Rem.FlexiBeeSDK`'s own `IDepartmentClient` — confirmed unrelated to the moved Domain types.
- Introducing a new `Anela.Heblo.Domain.Features.OrgChart` module (mentioned as an alternative in the issue) — the existing `Analytics` folder is the natural, lower-risk home and is used here.
- Any InvoiceClassification code change — module confirmed to have zero consumers of the moved types.

## Open Questions
None.
