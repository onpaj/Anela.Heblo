# Specification: Move `IDepartmentClient` / `Department` from Analytics to UserManagement domain

## Summary
`IDepartmentClient` and `Department` currently live in `Anela.Heblo.Domain.Features.Analytics`, but their only real consumer is `FlexiDepartmentQueryService`, which serves UserManagement's department-lookup flow. This is a pure module-boundary correction: relocate both types to a new `Anela.Heblo.Domain.Features.UserManagement` namespace/folder and update the small set of references that point at them. No behavior, data, or public API changes.

## Background
Architecture review (issue #4218, filed 2026-09-17 by the daily arch-review routine) found that `IDepartmentClient`/`Department` are namespaced under Analytics but have zero Analytics consumers:

- `DepartmentSyncService` (`backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/DepartmentSyncService.cs`) — the actual Analytics-module consumer of department data — injects `Rem.FlexiBeeSDK.Client.Clients.Accounting.Departments.IDepartmentClient` directly (the FlexiBee SDK client) and maps into its own `Persistence.Analytics.Entities.Department` entity. It never references `Domain.Features.Analytics.IDepartmentClient` or `Domain.Features.Analytics.Department`.
- The sole consumer of `Domain.Features.Analytics.IDepartmentClient`/`Department` is `FlexiDepartmentQueryService` (`backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/Departments/FlexiDepartmentQueryService.cs`), which implements UserManagement's `IDepartmentQueryService` (`Anela.Heblo.Application.Features.UserManagement.Services`) and maps `Domain.Features.Analytics.Department` → `Application.Features.UserManagement.Contracts.DepartmentDto`.

Placing the interface/model under the Analytics namespace misleads anyone tracing UserManagement's department data flow, and gives the Analytics domain a dependency surface (a FlexiBee-backed department client abstraction) it never uses. This confirms the brief's finding exactly; the fix is a same-behavior relocation, not a redesign.

Note: `Anela.Heblo.Domain.Features.UserManagement` does not exist yet as a folder/namespace, but it is already referenced as a forbidden-namespace target in `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` (the "Authorization -> UserManagement" rule, line ~380), so this move aligns with, rather than introduces, the project's intended module layout. It is distinct from the pre-existing `Anela.Heblo.Domain.Features.Users` folder (which holds `ICurrentUserService`/`CurrentUser` — the "who is logged in" concept, unrelated to this change).

## Functional Requirements

### FR-1: Relocate `Department` domain model
Move `backend/src/Anela.Heblo.Domain/Features/Analytics/Department.cs` to `backend/src/Anela.Heblo.Domain/Features/UserManagement/Department.cs`, changing its namespace from `Anela.Heblo.Domain.Features.Analytics` to `Anela.Heblo.Domain.Features.UserManagement`. No members change.

**Acceptance criteria:**
- File exists at the new path with the new namespace; no file remains at the old path.
- Class shape (`Id`, `Name`, both `string`, default `string.Empty`) is unchanged.

### FR-2: Relocate `IDepartmentClient` domain interface
Move `backend/src/Anela.Heblo.Domain/Features/Analytics/IDepartmentClient.cs` to `backend/src/Anela.Heblo.Domain/Features/UserManagement/IDepartmentClient.cs`, changing its namespace to `Anela.Heblo.Domain.Features.UserManagement`. No member changes.

**Acceptance criteria:**
- File exists at the new path with the new namespace; no file remains at the old path.
- Interface members (`GetDepartmentsAsync`, `GetDepartmentByIdAsync`) are unchanged.

### FR-3: Update `FlexiDepartmentClient` to reference the new namespace
`backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/Departments/FlexiDepartmentClient.cs` currently has `using Anela.Heblo.Domain.Features.Analytics;` and implements `Domain.Features.Analytics.IDepartmentClient` (fully qualified, to disambiguate from the FlexiBee SDK's own `IDepartmentClient`, which it aliases via `using IDepartmentClient = Rem.FlexiBeeSDK.Client.Clients.Accounting.Departments.IDepartmentClient;`). Update the `using` and the fully-qualified base-interface reference to `Anela.Heblo.Domain.Features.UserManagement`.

**Acceptance criteria:**
- `using Anela.Heblo.Domain.Features.Analytics;` replaced with `using Anela.Heblo.Domain.Features.UserManagement;`.
- Class declaration `FlexiDepartmentClient : Domain.Features.Analytics.IDepartmentClient` updated to `Domain.Features.UserManagement.IDepartmentClient`.
- The existing FlexiBee-SDK `IDepartmentClient` alias is untouched.
- `GetDepartmentsAsync`/`GetDepartmentByIdAsync` return the relocated `Department` type; compiles without ambiguity.

### FR-4: Update `FlexiDepartmentQueryService` to reference the new namespace
`backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Accounting/Departments/FlexiDepartmentQueryService.cs` has `using Anela.Heblo.Domain.Features.Analytics;` for its `IDepartmentClient` constructor dependency and its use of `Department` in the mapping LINQ. Update to `using Anela.Heblo.Domain.Features.UserManagement;`.

**Acceptance criteria:**
- `using` statement updated; no other line in the file changes.
- Mapping logic (`Department` → `DepartmentDto`) is unchanged.

### FR-5: Update DI registration in `FlexiAdapterServiceCollectionExtensions`
`backend/src/Adapters/Anela.Heblo.Adapters.Flexi/FlexiAdapterServiceCollectionExtensions.cs` has `using Anela.Heblo.Domain.Features.Analytics;` (line 30), used solely to resolve `IDepartmentClient` at `services.AddScoped<IDepartmentClient, FlexiDepartmentClient>();` (line 91). Update the `using` to `Anela.Heblo.Domain.Features.UserManagement`.

**Acceptance criteria:**
- `using Anela.Heblo.Domain.Features.Analytics;` is removed only if no other symbol in the file still needs it (confirmed: no other Analytics-domain type is referenced in this file); replaced with `using Anela.Heblo.Domain.Features.UserManagement;`.
- `services.AddScoped<IDepartmentClient, FlexiDepartmentClient>();` line itself is unchanged (it resolves via the new `using`).
- `services.AddScoped<IDepartmentQueryService, FlexiDepartmentQueryService>();` (already using `Anela.Heblo.Application.Features.UserManagement.Services`) is untouched — out of scope for this move.

### FR-6: Update test references
`backend/test/Anela.Heblo.Adapters.Flexi.Tests/Accounting/Departments/FlexiDepartmentQueryServiceTests.cs` has `using Anela.Heblo.Domain.Features.Analytics;` and constructs `Mock<IDepartmentClient>` plus `new List<Department> { ... }` against that namespace. Update the `using` to `Anela.Heblo.Domain.Features.UserManagement`.

**Acceptance criteria:**
- Test file compiles and all existing assertions pass unchanged after the namespace update.
- `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/DepartmentSyncServiceTests.cs` is explicitly verified to need **no** change — it only uses `Rem.FlexiBeeSDK...IDepartmentClient` and `Anela.Heblo.Persistence.Analytics.Entities.Department` (a distinct type from the one being moved), consistent with the brief's note that `DepartmentSyncService` is unaffected.

### FR-7: Full-repository sweep for stray references
Search the whole repo (`backend/src`, `backend/test`) for any remaining reference to `Anela.Heblo.Domain.Features.Analytics.IDepartmentClient` or `Anela.Heblo.Domain.Features.Analytics.Department` (fully qualified or via `using`) beyond the five files identified in FR-3–FR-6, and update any found. As of this spec, exhaustive grep confirms exactly these files reference the two types; no others were found (module-boundary architecture test `ModuleBoundariesTests.cs` does not reference either type by name).

**Acceptance criteria:**
- `grep -rn "Domain.Features.Analytics.IDepartmentClient\|Domain.Features.Analytics.Department\b"` across `backend/` returns no matches after the change (excluding false positives like `AnalyticsProductExtensions`, `AnalyticsProduct`, etc., which are unrelated types).

## Non-Functional Requirements

### NFR-1: Performance
Not applicable — this is a namespace/file relocation with no logic, allocation, or I/O changes. No performance impact expected or to be tested.

### NFR-2: Security
Not applicable — no change to authentication, authorization, data exposure, or secrets handling. `Department`/`IDepartmentClient` carry no sensitive data beyond department id/name already exposed via the existing `GET` department endpoint.

### NFR-3: Build integrity
The change must not break `dotnet build` or `dotnet format` for the solution, and must not introduce new violations in `ModuleBoundariesTests.cs` (the architecture-fitness test suite). Since `Anela.Heblo.Domain.Features.UserManagement` is already a recognized forbidden-namespace target in that suite's `Authorization -> UserManagement` rule, creating the folder does not require adding a new rule — but the build must be run to confirm no existing rule newly fires (e.g. nothing under `Analytics` accidentally starts referencing `UserManagement`, and vice versa nothing pre-existing under `Authorization` picks up a transitive reference).

## Data Model
No persisted data model changes. `Department` (domain model, in-memory only, not EF-mapped) moves namespace but keeps identical shape:

```csharp
public class Department
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
```

This is unrelated to `Anela.Heblo.Persistence.Analytics.Entities.Department` (the FlexiBee-sync-owned EF entity with `FlexiId`, `Code`, `LastModified`, `RawPayload`, etc.), which is untouched by this change and continues to live under Analytics persistence, since `DepartmentSyncService` genuinely is an Analytics-owned flow.

## API / Interface Design
No externally visible API changes (no controller routes, MediatR contracts, or DTOs change). Internal interface relocation only:

- **Before:** `Anela.Heblo.Domain.Features.Analytics.IDepartmentClient`, `Anela.Heblo.Domain.Features.Analytics.Department`
- **After:** `Anela.Heblo.Domain.Features.UserManagement.IDepartmentClient`, `Anela.Heblo.Domain.Features.UserManagement.Department`

Consumer chain (unchanged in shape, just namespace-corrected):
`GetDepartmentsHandler` (Application/UserManagement) → `IDepartmentQueryService` (Application/UserManagement/Services) → `FlexiDepartmentQueryService` (Adapters.Flexi) → `IDepartmentClient` (now Domain/UserManagement) → `FlexiDepartmentClient` (Adapters.Flexi) → FlexiBee SDK's own `IDepartmentClient`.

## Dependencies
- No new external dependencies.
- No dependency on `docs/integrations/shoptet-api.md` (this touches FlexiBee, not Shoptet).
- Depends only on the existing `Anela.Heblo.Domain` project structure; creating the `Features/UserManagement/` folder there is additive (no existing folder/namespace collision, confirmed by inspection).

## Out of Scope
- Renaming or restructuring the existing `Anela.Heblo.Domain.Features.Users` folder (unrelated "current user" concept) — no interaction with this change.
- Any change to `DepartmentSyncService`, its FlexiBee SDK usage, or `Persistence.Analytics.Entities.Department` — brief explicitly confirms these are unaffected.
- Any change to `IDepartmentQueryService` or `DepartmentDto` (already correctly namespaced under UserManagement's Application layer).
- Adding a new `ModuleBoundariesTests.cs` rule constraining `Domain.Features.UserManagement` — the relevant forbidding rule (Authorization -> UserManagement) already exists and already names this namespace; no new rule is required by this change.
- Any functional/behavioral change to department lookup, caching, or the FlexiBee integration.

## Open Questions
None.

## Status: COMPLETE
