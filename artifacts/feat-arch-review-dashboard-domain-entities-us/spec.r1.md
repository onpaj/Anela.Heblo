# Specification: Relocate Dashboard Domain Entities from Xcc to Domain Layer

## Summary
Move two Dashboard domain entities (`UserDashboardSettings`, `UserDashboardTile`) and one repository interface (`IUserDashboardSettingsRepository`) from the `Anela.Heblo.Xcc` cross-cutting concerns project to their canonical location in `Anela.Heblo.Domain/Features/Dashboard/`. This is a pure refactoring with no behavioral changes — it aligns the Dashboard module with the layering conventions used by every other feature module and prepares for the per-module DbContext split planned in ADR-001 Phase 2.

## Background
The Dashboard module currently violates the project's Clean Architecture layering. Dashboard-specific domain types live in `Anela.Heblo.Xcc/Domain/` and `Anela.Heblo.Xcc/Services/Dashboard/`, but `Xcc` is reserved for technical cross-cutting concerns (per `CLAUDE.md`: "Use Xcc for technical concerns only"). Every other module — Catalog, Photobank, Plaud, Logistics, etc. — places feature-specific entities under `Anela.Heblo.Domain/Features/{Module}/`.

Consequences of the misplacement:
- `DashboardService` (Application layer) depends on Xcc for what should be Domain types, making Xcc a middleman with no architectural justification.
- The pattern blocks the natural Application → Domain dependency direction and forces Application code to reach into a cross-cutting project for feature logic.
- When the codebase moves to per-module DbContexts (ADR-001 Phase 2), these entities would need to migrate anyway — fixing now avoids a double move and any downstream churn it would cause.

The fix is mechanical: three files move, namespaces update, `using` statements update across the affected callers. No public API, schema, or behavior changes.

## Functional Requirements

### FR-1: Move `UserDashboardSettings` to the Domain layer
Move `backend/src/Anela.Heblo.Xcc/Domain/UserDashboardSettings.cs` to `backend/src/Anela.Heblo.Domain/Features/Dashboard/UserDashboardSettings.cs`. Update the file's namespace from `Anela.Heblo.Xcc.Domain` (or whatever it currently declares) to `Anela.Heblo.Domain.Features.Dashboard`. Class definition, properties, and behavior remain unchanged.

**Acceptance criteria:**
- The file no longer exists at the old path.
- The file exists at the new path with the correct namespace `Anela.Heblo.Domain.Features.Dashboard`.
- The class members (properties, methods, constructors) are byte-identical to the prior version aside from the namespace declaration.
- `dotnet build` succeeds for the whole solution.

### FR-2: Move `UserDashboardTile` to the Domain layer
Move `backend/src/Anela.Heblo.Xcc/Domain/UserDashboardTile.cs` to `backend/src/Anela.Heblo.Domain/Features/Dashboard/UserDashboardTile.cs`. Update the namespace to `Anela.Heblo.Domain.Features.Dashboard`.

**Acceptance criteria:**
- The file no longer exists at the old path.
- The file exists at the new path with namespace `Anela.Heblo.Domain.Features.Dashboard`.
- Class members are unchanged aside from namespace.
- `dotnet build` succeeds.

### FR-3: Move `IUserDashboardSettingsRepository` to the Domain layer
Move `backend/src/Anela.Heblo.Xcc/Services/Dashboard/IUserDashboardSettingsRepository.cs` to `backend/src/Anela.Heblo.Domain/Features/Dashboard/IUserDashboardSettingsRepository.cs`. Update the namespace to `Anela.Heblo.Domain.Features.Dashboard`.

**Acceptance criteria:**
- The file no longer exists at the old path.
- The file exists at the new path with namespace `Anela.Heblo.Domain.Features.Dashboard`.
- Interface signature (method names, parameters, return types) is unchanged.
- `dotnet build` succeeds.

### FR-4: Update consumer `using` statements
Update every file that references the relocated types so it imports `Anela.Heblo.Domain.Features.Dashboard` instead of the prior Xcc namespaces. Known callers per the brief:
- `DashboardService.cs` (Application layer)
- `SaveUserSettingsHandler.cs`
- `EnableTileHandler.cs`
- The EF Core persistence configuration files for `UserDashboardSettings` and `UserDashboardTile` (in `Persistence/Dashboard/Configurations/` or equivalent)
- The repository implementation in `Persistence/Dashboard/`

A repository-wide search (`grep`) for `Anela.Heblo.Xcc.Domain` and `Anela.Heblo.Xcc.Services.Dashboard` MUST be performed to catch any callers the brief did not enumerate (test projects, DI registrations, additional handlers, etc.).

**Acceptance criteria:**
- No file in the solution still references `Anela.Heblo.Xcc.Domain.UserDashboardSettings`, `Anela.Heblo.Xcc.Domain.UserDashboardTile`, or `Anela.Heblo.Xcc.Services.Dashboard.IUserDashboardSettingsRepository`.
- All updated files compile.
- `dotnet build` succeeds for the entire solution including test projects.

### FR-5: Remove vacated Xcc directories if empty
After the moves, if `backend/src/Anela.Heblo.Xcc/Domain/` and/or `backend/src/Anela.Heblo.Xcc/Services/Dashboard/` are empty, remove the empty directories. If they still contain other files, leave them in place untouched.

**Acceptance criteria:**
- No empty directories left behind under `Anela.Heblo.Xcc/`.
- Any non-Dashboard files that were already in those directories are left exactly as found.

### FR-6: Preserve all existing behavior
This refactor is structural only. There must be no change to:
- Database schema or EF migrations.
- API contracts, DTOs, or generated OpenAPI clients.
- Runtime behavior of the Dashboard feature (tile enablement, settings persistence, retrieval).
- Dependency injection registrations beyond namespace updates.

**Acceptance criteria:**
- No new EF Core migration is generated.
- No regenerated OpenAPI client diff.
- All existing Dashboard unit and integration tests pass without modification (except for any `using` updates).
- Manual smoke check: starting the backend and loading the dashboard returns the same tiles and settings as before the change.

## Non-Functional Requirements

### NFR-1: Build & Lint
The change MUST satisfy the project's validation gates from `CLAUDE.md`:
- `dotnet build` succeeds with no new warnings.
- `dotnet format` reports no required changes (or applied changes are committed).
- All existing backend tests pass: `dotnet test` for the solution.

### NFR-2: Surgical Scope
Only namespace declarations, `using` directives, and file paths may change. No member renames, no signature changes, no comment cleanup, no formatting churn in unrelated lines. The diff per file should be minimal — ideally one-line namespace edit plus the file move, and one-line `using` edits in callers.

### NFR-3: Architectural Conformance
After this change, `Anela.Heblo.Xcc` MUST contain no Dashboard-specific types. A `grep` for "Dashboard" under `backend/src/Anela.Heblo.Xcc/` should return no feature-domain matches (incidental string occurrences in generic infrastructure code are acceptable; entity classes, repository interfaces, or feature services are not).

### NFR-4: No Layer-Direction Regressions
Verify that after the move, the Domain project does not depend on the Application or Infrastructure projects (it should already not — this requirement is a guard against accidentally introducing such a reference through misplaced types). The Domain project's `.csproj` references should be unchanged by this work.

## Data Model

No data model changes. The relocated types retain their existing shape:

- **`UserDashboardSettings`** — aggregate root holding a user's dashboard configuration; owns a collection of `UserDashboardTile`.
- **`UserDashboardTile`** — entity representing one configurable tile on a user's dashboard (visibility, ordering, etc., per the current implementation).
- **`IUserDashboardSettingsRepository`** — repository abstraction for loading and persisting `UserDashboardSettings`.

The EF Core mappings, table names, columns, and indexes remain identical. No migration is required because the CLR namespace is not part of the persistence schema.

## API / Interface Design

No external API changes. No new endpoints. No DTO changes. No event contract changes.

Internal interface changes are limited to namespace relocation:

| Before | After |
|---|---|
| `Anela.Heblo.Xcc.Domain.UserDashboardSettings` | `Anela.Heblo.Domain.Features.Dashboard.UserDashboardSettings` |
| `Anela.Heblo.Xcc.Domain.UserDashboardTile` | `Anela.Heblo.Domain.Features.Dashboard.UserDashboardTile` |
| `Anela.Heblo.Xcc.Services.Dashboard.IUserDashboardSettingsRepository` | `Anela.Heblo.Domain.Features.Dashboard.IUserDashboardSettingsRepository` |

## Dependencies

- **Anela.Heblo.Domain project** — must already reference whatever base abstractions (`AggregateRoot`, `Entity`, etc.) the relocated types use. If the relocated types currently depend on a type that lives in `Xcc`, that dependency relationship must be evaluated:
  - If the dependency is a generic technical base (e.g., a base `Entity<TKey>` class), Domain referencing Xcc is acceptable per current project conventions — verify Domain already has this reference.
  - If the dependency itself should be in Domain, that is out of scope for this brief and should be raised as a separate finding.
- **EF Core configuration files** — must be updated to import the new namespace; no other configuration changes required.
- **No external service, library, or feature dependencies.**

## Out of Scope

- Refactoring `DashboardService`, `SaveUserSettingsHandler`, or `EnableTileHandler` beyond the `using` statement updates.
- Splitting the Dashboard module into its own DbContext (ADR-001 Phase 2 — separate work item).
- Issue #1943 (`DashboardService` placement) — explicitly out of scope; this brief only enables that future work, it does not perform it.
- Auditing other modules for similar layering violations.
- Renaming or reshaping any of the relocated types' members.
- Frontend changes — none required, the API surface is unaffected.
- Database migrations — none required.
- Documentation updates beyond what is necessary if a doc explicitly references the old paths (search `docs/` for `Xcc/Domain/UserDashboardSettings`, etc., and update only exact references).

## Open Questions

None.

## Status: COMPLETE