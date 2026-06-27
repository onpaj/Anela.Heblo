# Specification: Move BackgroundRefresh DTOs out of BackgroundJobs module

## Summary

Three response DTOs (`RefreshTaskDto`, `RefreshTaskStatusDto`, `RefreshTaskExecutionLogDto`) currently live in `Application/Features/BackgroundJobs/Contracts/` despite belonging exclusively to `BackgroundRefreshController` and the Xcc `IBackgroundRefreshTaskRegistry` infrastructure. This specification defines the work to relocate those DTOs to a new `BackgroundRefresh` Application module, eliminating the ownership violation and preparing the ground for proper MediatR handler coverage of the BackgroundRefresh HTTP surface.

## Background

The project's development guidelines mandate that DTO objects for API request/response live in the `contracts/` folder of the module that owns them, and that DTOs are never shared or global. `BackgroundRefreshController` is a separate controller wired directly to `IBackgroundRefreshTaskRegistry` (a cross-cutting Xcc service). It has no relationship to the Hangfire-based BackgroundJobs module whose handlers (`GetRecurringJobsListHandler`, `TriggerRecurringJobHandler`, `UpdateRecurringJobCronHandler`, `UpdateRecurringJobStatusHandler`) never reference the three misplaced types.

Because there is no `BackgroundRefresh` Application module today, the DTOs were placed in the nearest available `Contracts/` folder — BackgroundJobs — which is incorrect. The misplacement:

- Pollutes BackgroundJobs with unowned contracts, confusing developers navigating that module.
- Couples any future BackgroundRefresh HTTP contract change to a BackgroundJobs edit.
- Contradicts the rule that each module owns its contracts (development_guidelines.md, "Mandatory Rules").

The brief proposes Option A as preferred: create a `BackgroundRefresh` Application module and move the DTOs there. Option B (placing them in `Xcc/Services/BackgroundRefresh/Dto/`) is explicitly out of scope because the development guidelines forbid DTOs defined in Xcc.

## Functional Requirements

### FR-1: Create `BackgroundRefresh` Application module folder

Create the directory `backend/src/Anela.Heblo.Application/Features/BackgroundRefresh/` with a `Contracts/` subdirectory. The module follows the same layout as every other Application feature module.

**Acceptance criteria:**
- Directory `Application/Features/BackgroundRefresh/Contracts/` exists.
- A `BackgroundRefreshModule.cs` file exists at `Application/Features/BackgroundRefresh/BackgroundRefreshModule.cs` and exposes an `AddBackgroundRefreshModule(this IServiceCollection)` extension method.
- `Program.cs` (or the central composition root) calls `AddBackgroundRefreshModule()`.

### FR-2: Move the three DTOs into the new module

Move the following files verbatim (content unchanged, only namespace updated):

| Source | Destination |
|---|---|
| `BackgroundJobs/Contracts/RefreshTaskDto.cs` | `BackgroundRefresh/Contracts/RefreshTaskDto.cs` |
| `BackgroundJobs/Contracts/RefreshTaskStatusDto.cs` | `BackgroundRefresh/Contracts/RefreshTaskStatusDto.cs` |
| `BackgroundJobs/Contracts/RefreshTaskExecutionLogDto.cs` | `BackgroundRefresh/Contracts/RefreshTaskExecutionLogDto.cs` |

The namespace of each file changes from `Anela.Heblo.Application.Features.BackgroundJobs.Contracts` to `Anela.Heblo.Application.Features.BackgroundRefresh.Contracts`.

**Acceptance criteria:**
- All three files exist at the new paths with the updated namespace declaration.
- All three files are deleted from `BackgroundJobs/Contracts/`.
- The `BackgroundJobs/Contracts/` folder retains only `RecurringJobDto.cs`, `UpdateJobCronRequestBody.cs`, and `UpdateJobStatusRequestBody.cs`.

### FR-3: Update `BackgroundRefreshController` to import from the new namespace

`BackgroundRefreshController.cs` currently has:
```csharp
using Anela.Heblo.Application.Features.BackgroundJobs.Contracts;
```
Replace that `using` directive with:
```csharp
using Anela.Heblo.Application.Features.BackgroundRefresh.Contracts;
```

No other changes to the controller are required by this task.

**Acceptance criteria:**
- `BackgroundRefreshController.cs` contains no reference to `BackgroundJobs.Contracts`.
- The controller compiles without errors and its five existing endpoints remain functionally identical.

### FR-4: Verify no other consumers reference the old namespace

Search the entire solution for any remaining references to `Anela.Heblo.Application.Features.BackgroundJobs.Contracts.RefreshTask` and confirm there are none. The three DTO types must no longer be reachable under the old namespace.

**Acceptance criteria:**
- `grep -r "BackgroundJobs.Contracts.RefreshTask" backend/` returns zero results.
- `grep -r "BackgroundJobs.Contracts.RefreshTask" frontend/` returns zero results (TypeScript client will regenerate).
- `dotnet build` succeeds with no errors or warnings related to the moved types.

### FR-5: Regenerate the OpenAPI TypeScript client

The OpenAPI TypeScript client is auto-generated on build. After the backend build succeeds, confirm the generated client reflects the unchanged API surface (same endpoints, same schema — only the C# namespace changed, not the JSON shape).

**Acceptance criteria:**
- `npm run build` (frontend) completes without TypeScript errors.
- The generated API client files do not show schema changes — only namespace-derived comment/tag changes, if any, are acceptable.

## Non-Functional Requirements

### NFR-1: Performance

No performance impact. This is a pure structural refactoring with no logic changes.

### NFR-2: Security

No security impact. Authorization attributes on `BackgroundRefreshController` (`[FeatureAuthorize(Feature.Admin_Administration)]`) are untouched.

### NFR-3: Backward compatibility

The HTTP API surface (`/api/backgroundrefresh/...`) is unchanged. JSON response shapes for `RefreshTaskDto`, `RefreshTaskStatusDto`, and `RefreshTaskExecutionLogDto` must remain byte-for-byte identical. No database migration is required.

### NFR-4: Test coverage

No existing tests reference the three DTOs directly (they live in Xcc integration tests that use the registry types, not the Application DTOs). If any test file is found to import from `BackgroundJobs.Contracts` for these types, it must be updated to the new namespace. No new tests are required for this refactoring task alone.

## Data Model

No data model changes. The DTOs are pure mapping targets used to serialize `RefreshTaskConfiguration` and `RefreshTaskExecutionLog` (Xcc types) into HTTP responses. Those Xcc types are unchanged.

| DTO | Maps from (Xcc type) |
|---|---|
| `RefreshTaskDto` | `RefreshTaskConfiguration` + `RefreshTaskExecutionLog?` |
| `RefreshTaskStatusDto` | `RefreshTaskConfiguration` + `RefreshTaskExecutionLog?` |
| `RefreshTaskExecutionLogDto` | `RefreshTaskExecutionLog` |

## API / Interface Design

No changes to API routes, request shapes, or response shapes. The five existing `BackgroundRefreshController` endpoints are unchanged:

| Method | Route | Response DTO |
|---|---|---|
| GET | `/api/backgroundrefresh/tasks` | `IEnumerable<RefreshTaskDto>` |
| GET | `/api/backgroundrefresh/tasks/{taskId}/history` | `IEnumerable<RefreshTaskExecutionLogDto>` |
| GET | `/api/backgroundrefresh/history` | `IEnumerable<RefreshTaskExecutionLogDto>` |
| POST | `/api/backgroundrefresh/tasks/{taskId}/force-refresh` | anonymous object |
| POST | `/api/backgroundrefresh/tiers/{tier}/run` | anonymous object |
| GET | `/api/backgroundrefresh/tasks/{taskId}/status` | `RefreshTaskStatusDto` |

The `BackgroundRefreshModule.AddBackgroundRefreshModule()` method has no services to register at this stage (no MediatR handlers, no repositories). It exists as a DI registration hook to give the module a proper composition-root entry point consistent with all other Application modules.

Assumption: `BackgroundRefreshModule` is registered in `Program.cs` alongside the other module registrations (e.g., `.AddBackgroundRefreshModule()`). If a central `ApplicationModule.cs` aggregates all feature modules, it must be updated there instead.

## Dependencies

- `Anela.Heblo.Xcc` — `IBackgroundRefreshTaskRegistry`, `RefreshTaskConfiguration`, `RefreshTaskExecutionLog`, `RefreshTaskExecutionStatus` remain unchanged and are still consumed by `BackgroundRefreshController` directly.
- `Anela.Heblo.Application` — new `BackgroundRefresh` feature module added here.
- `Anela.Heblo.API` — `BackgroundRefreshController` updated to use new namespace; composition root updated to call `AddBackgroundRefreshModule()`.
- OpenAPI TypeScript client regeneration via existing `npm run build` pipeline.

## Out of Scope

- Adding MediatR handlers to the new `BackgroundRefresh` module. The controller currently bypasses MediatR and calls `IBackgroundRefreshTaskRegistry` directly; correcting that pattern is a separate architectural concern.
- Moving `BackgroundRefreshController.cs` from `Controllers/` to a feature sub-folder.
- Changing any field, property, or JSON serialization behavior of the three DTOs.
- Adding a `RefreshTaskDto` description field to `RefreshTaskStatusDto` (the current `Description` property is already present but not populated — this is an existing gap, not introduced here).
- Any frontend UI changes.
- Database migrations.
- Option B from the brief (placing DTOs in `Xcc/Services/BackgroundRefresh/Dto/`). The development guidelines explicitly forbid DTOs in Xcc.

## Open Questions

None.

## Status: COMPLETE
