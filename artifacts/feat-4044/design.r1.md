# Design: Relocate RecurringJobStatusChecker into BackgroundJobs/Services/

## Component Design
No new or redesigned components. `RecurringJobStatusChecker` (Application-layer implementation of the Domain-owned `IRecurringJobStatusChecker`) is relocated one directory level down, from the `BackgroundJobs` module root into its sibling `Services/` folder, alongside the module's other service implementations (`RecurringJobSeeder`, etc.). Its responsibility, constructor, and method bodies are unchanged.

- File move: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/RecurringJobStatusChecker.cs` → `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobStatusChecker.cs`.
- Namespace change: `Anela.Heblo.Application.Features.BackgroundJobs` → `Anela.Heblo.Application.Features.BackgroundJobs.Services`.
- `using` directive updates:
  - `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobStatusCheckerTests.cs` — change `using Anela.Heblo.Application.Features.BackgroundJobs;` to `using Anela.Heblo.Application.Features.BackgroundJobs.Services;`.
  - `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/BackgroundJobsModule.cs` — already imports `Anela.Heblo.Application.Features.BackgroundJobs.Services`; no change expected, but re-verify after the move via `dotnet build`.
- The `IRecurringJobStatusChecker` interface (`Anela.Heblo.Domain.Features.BackgroundJobs`) and all other consumers referencing it only via that interface are unaffected.

## Data Schemas
No schema changes. This is a pure code-organization move — no database, API, or event payload shapes are introduced, removed, or modified.
