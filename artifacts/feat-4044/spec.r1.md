# Specification: Relocate RecurringJobStatusChecker into BackgroundJobs/Services/

## Summary
`RecurringJobStatusChecker` is the sole Application-layer service implementation in the `BackgroundJobs` module that lives at the module root instead of alongside its sibling service implementations in `BackgroundJobs/Services/`. This is a mechanical, no-logic-change relocation: move the file, update its namespace, and fix any now-stale `using` directives so the build and existing tests continue to pass.

## Background
`docs/architecture/filesystem.md` and the module's own established convention place service implementations under `Features/BackgroundJobs/Services/` (e.g. `ICronScheduler`, `IFailedJobCounter`, `IJobEnqueuer`, `IRecurringJobSeeder`, `RecurringJobSeeder`). `RecurringJobStatusChecker` — the Application-layer implementation of the Domain-owned `IRecurringJobStatusChecker` interface — was left at `Features/BackgroundJobs/RecurringJobStatusChecker.cs` with namespace `Anela.Heblo.Application.Features.BackgroundJobs`, following the same service pattern as `RecurringJobSeeder` but orphaned at the module root. This was flagged by the daily arch-review routine. Fixing it keeps the module root limited to mapping profiles, module registration, and the next-run calculator utility, and keeps the location predictable for developers browsing the module.

## Functional Requirements

### FR-1: Move RecurringJobStatusChecker into Services/ and update its namespace
Relocate the implementation file and namespace, and update every reference so the codebase builds and all existing tests pass with no behavioral change.

Concretely:
- Move `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/RecurringJobStatusChecker.cs` to `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobStatusChecker.cs`.
- Change its namespace declaration from `Anela.Heblo.Application.Features.BackgroundJobs` to `Anela.Heblo.Application.Features.BackgroundJobs.Services`.
- Leave the class name, its implementation of `IRecurringJobStatusChecker` (`Anela.Heblo.Domain.Features.BackgroundJobs`), constructor, method bodies, and all logic byte-for-byte unchanged — this is a location/namespace change only.
- Update `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/BackgroundJobsModule.cs` if it needs a new `using Anela.Heblo.Application.Features.BackgroundJobs.Services;` — note it already carries this `using` (for `RecurringJobSeeder`), so the registration line `services.AddScoped<IRecurringJobStatusChecker, RecurringJobStatusChecker>();` likely needs no change, but must be re-verified after the move.
- Update any other file that references the type by its old namespace and does not already import `Anela.Heblo.Application.Features.BackgroundJobs.Services`. This includes at minimum `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobStatusCheckerTests.cs`, which currently has `using Anela.Heblo.Application.Features.BackgroundJobs;` and will need `using Anela.Heblo.Application.Features.BackgroundJobs.Services;` instead (or in addition, if the file also references other root-level module types).
- Search the solution (source and tests) for any remaining unqualified/qualified references to the old namespace for this type to ensure nothing is missed.

**Acceptance criteria:**
- `RecurringJobStatusChecker.cs` exists at `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobStatusChecker.cs` and no longer exists at the old module-root path.
- The file's namespace is `Anela.Heblo.Application.Features.BackgroundJobs.Services`.
- `dotnet build` succeeds for the whole solution with no new warnings or errors introduced by this change.
- `dotnet format` reports no issues for the touched files.
- All existing tests referencing `RecurringJobStatusChecker` (including `RecurringJobStatusCheckerTests.cs`) compile and pass unchanged in behavior/assertions — only `using` directives may change.
- No other source or test file is left with a broken/stale reference to the old namespace for this type.
- No public behavior, DI registration outcome, or method signature changes as a result of this move.

## Non-Functional Requirements

### NFR-1: Performance
N/A — this is a file/namespace relocation with no runtime behavior change.

### NFR-2: Security
N/A — no change to authentication, authorization, or data handling.

## Data Model
N/A — no entities or persistence are affected by this change.

## API / Interface Design
N/A — no public API, controller, or contract changes. The `IRecurringJobStatusChecker` interface (Domain layer) and its DI registration remain unchanged; only the implementation's file location and namespace move.

## Dependencies
None beyond the existing `Anela.Heblo.Domain.Features.BackgroundJobs.IRecurringJobStatusChecker` interface and the module's existing DI setup in `BackgroundJobsModule.cs`.

## Out of Scope
- Any change to `RecurringJobStatusChecker`'s logic, method signatures, or dependencies.
- Renaming or relocating any other file in the `BackgroundJobs` module.
- Changes to the `IRecurringJobStatusChecker` interface itself.

## Open Questions
None.

## Status: COMPLETE
