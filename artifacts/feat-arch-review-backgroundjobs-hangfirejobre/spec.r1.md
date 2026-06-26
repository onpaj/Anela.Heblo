# Specification: Move HangfireJobRegistrationHelper to API Layer

## Summary
Relocate `HangfireJobRegistrationHelper` from the Application layer to the API layer to restore the Clean Architecture boundary. The Application project must not reference `Hangfire` (an infrastructure concern); all Hangfire static API calls and registration helpers must live alongside the other Hangfire adapter code in the API project.

## Background
The `BackgroundJobs` module follows Clean Architecture: Application defines abstractions (`IHangfireJobEnqueuer`, `IHangfireRecurringJobScheduler`) and the API project provides the Hangfire-backed implementations (registered in `Anela.Heblo.API.Extensions.ServiceCollectionExtensions.AddHangfireServices`). The module's own `BackgroundJobsModule.cs` documents this rule explicitly.

However, `HangfireJobRegistrationHelper` — a public static class in `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/HangfireJobRegistrationHelper.cs` — violates this rule:
- It has `using Hangfire;` at line 4
- It calls `RecurringJob.AddOrUpdate<TJob>(...)` directly (lines 52–68)
- It uses reflection to dispatch to `RegisterOrUpdateGeneric<TJob>`, which calls the Hangfire static API (lines 71–86)

This creates a compile-time dependency from `Anela.Heblo.Application` → `Hangfire`, defeating the architectural boundary the rest of the codebase respects. It also blocks meaningful unit testing of registration logic because every call path needs a real Hangfire `JobStorage`.

The brief confirms the helper has **no callers in the Application project itself** — only startup discovery and runtime CRON-update code in the API/Infrastructure layer use it. The move is therefore a pure structural refactor.

## Functional Requirements

### FR-1: Relocate HangfireJobRegistrationHelper to the API project
Move the file from
`backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/HangfireJobRegistrationHelper.cs`
to
`backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireJobRegistrationHelper.cs`.

The class type, public surface (method names, generic parameters, signatures, accessibility), and behavior must be preserved exactly. Only the namespace changes (to match the new physical location, e.g. `Anela.Heblo.API.Infrastructure.Hangfire`).

**Acceptance criteria:**
- The file no longer exists under `Anela.Heblo.Application/Features/BackgroundJobs/Services/`.
- The file exists at `Anela.Heblo.API/Infrastructure/Hangfire/HangfireJobRegistrationHelper.cs`.
- The class declaration, method signatures, parameters, return types, and modifiers are unchanged.
- The namespace reflects the new location and matches the convention used by other types in `Anela.Heblo.API/Infrastructure/Hangfire/`.
- `git mv` (or equivalent rename in tooling) preserves history where possible.

### FR-2: Remove the Application → Hangfire compile-time dependency
After the move, the `Anela.Heblo.Application` project must not contain any `using Hangfire;` directives or references to Hangfire types. The Hangfire NuGet package reference (if it exists on the Application `.csproj` solely to support this helper) must be removed from `Anela.Heblo.Application.csproj`.

**Acceptance criteria:**
- `grep -r "using Hangfire" backend/src/Anela.Heblo.Application/` returns no results.
- `grep -r "Hangfire\." backend/src/Anela.Heblo.Application/` returns no infrastructure-type references (abstractions named `IHangfire*` defined in Application are not in scope).
- `Anela.Heblo.Application.csproj` does not declare a `PackageReference` to `Hangfire` or `Hangfire.*` unless another legitimate Application-layer use remains (verify and document).
- `dotnet build` succeeds for the full solution.

### FR-3: Update all call sites
Every caller of `HangfireJobRegistrationHelper` must be updated to reference the new namespace. The brief states all callers already live in the API/Infrastructure layer (startup discovery and runtime CRON update), so no cross-layer wiring changes are required.

**Acceptance criteria:**
- All references to `HangfireJobRegistrationHelper` compile against the new namespace.
- No `using Anela.Heblo.Application.Features.BackgroundJobs.Services;` (or equivalent legacy namespace) remains anywhere in the solution referring to this helper.
- Behavior at startup and at runtime CRON updates is unchanged (same jobs registered with the same CRON expressions, queues, and identifiers).

### FR-4: Update or relocate tests
Any unit/integration tests that target `HangfireJobRegistrationHelper` must be updated to reference the new namespace. If those tests currently live under an Application test project but the helper is now in the API layer, move them to the appropriate API test project. If no tests exist today, none are required by this refactor (testability improvements are out of scope — see Out of Scope).

**Acceptance criteria:**
- All existing tests for the helper continue to pass.
- No test references the legacy namespace.
- Test project layout matches the production project layout (tests for API-layer types live in the API test project).

### FR-5: Preserve runtime behavior
This is a non-functional refactor. CRON registration, job identifiers, queue assignments, retry attributes, and any reflection-based dispatch behavior must be byte-for-byte equivalent before and after the move.

**Acceptance criteria:**
- Startup logs list the same set of recurring jobs in the same order with the same CRON expressions.
- A smoke run against a local Hangfire dashboard shows identical job definitions to the pre-refactor baseline.
- No new warnings or errors appear in startup logs attributable to this change.

## Non-Functional Requirements

### NFR-1: Architecture
After the change, `Anela.Heblo.Application` must satisfy the Clean Architecture inward-dependency rule: no references to `Hangfire`, `Microsoft.AspNetCore.*` web infrastructure, or other API/Infrastructure-layer concerns. This refactor closes the specific Application → Hangfire violation; broader audits are out of scope.

### NFR-2: Build & validation
- `dotnet build` for the full solution must succeed with no new warnings.
- `dotnet format` must report no formatting changes after the move.
- All existing backend tests must continue to pass (`dotnet test`).

### NFR-3: Backwards compatibility
There is no public API exposed by this helper outside the backend solution (it is not used by the frontend, OpenAPI surface, or external consumers). No deprecation period or shim is required. The class is internal-to-codebase and can be moved cleanly.

### NFR-4: Source control
Use `git mv` (or an editor refactor that preserves history) so that `git log --follow` continues to surface the file's full history at the new path.

## Data Model
Not applicable. No database tables, entities, or persisted state are affected. No migrations are needed.

## API / Interface Design
Not applicable. No HTTP endpoints, MediatR contracts, or DTOs are added, removed, or modified. The change is purely structural inside the backend solution.

## Dependencies
- **Existing**: `Hangfire` NuGet package (already referenced by `Anela.Heblo.API`).
- **No new packages** are introduced.
- **Related code** (must remain in sync, but no change required by this spec): `BackgroundJobsModule.cs`, `Anela.Heblo.API.Extensions.ServiceCollectionExtensions.AddHangfireServices`, the `IHangfireJobEnqueuer` / `IHangfireRecurringJobScheduler` abstractions in Application and their API-layer implementations.

## Out of Scope
- **Testability improvements**: Introducing a wrapper around `RecurringJob.AddOrUpdate` to enable unit testing without a real Hangfire `JobStorage`. The brief mentions this as a benefit of moving the class, but the refactor itself does not require it. File a separate task if desired.
- **Broader Clean Architecture audit**: Other potential layering violations in the `BackgroundJobs` module or elsewhere are not addressed here.
- **Renaming the helper or splitting its responsibilities**: Generic dispatch via reflection and the `RegisterOrUpdateGeneric<TJob>` pattern remain as-is. Any redesign is a separate change.
- **Changing job registration semantics**: No CRON expressions, queue names, retry policies, or job IDs change.
- **Frontend changes**: None — this helper has no UI surface.

## Open Questions
None.

## Status: COMPLETE