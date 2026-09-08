# Architecture Review: Relocate RecurringJobStatusChecker into BackgroundJobs/Services/

## Skip Design: true

## Architectural Fit Assessment
This is a pure file/namespace relocation inside the Application layer's `BackgroundJobs` module — no new component, no behavior change, no layering violation to design around. `docs/architecture/filesystem.md` explicitly designates `Features/{Feature}/Services/` as the home for "domain services, background services" within a feature module. Verified against the actual module: every other service implementation (`ICronScheduler`, `IFailedJobCounter`, `IJobEnqueuer`, `IRecurringJobSeeder`, `RecurringJobSeeder`) already lives in `Features/BackgroundJobs/Services/`, and `RecurringJobStatusChecker.cs` is confirmed to be the sole class sitting at the module root instead. The fix simply makes the module internally consistent with its own established convention and the documented layout; there is no architectural tradeoff to weigh.

## Proposed Architecture

### Component Overview
No new components. `RecurringJobStatusChecker` (Application-layer implementation of the Domain-owned `IRecurringJobStatusChecker`) moves one directory level down, into the sibling `Services/` folder where its peers already live. Its relationship to `IRecurringJobStatusChecker` (`Anela.Heblo.Domain.Features.BackgroundJobs`) and to its consumers is unchanged.

### Key Design Decisions
There is no real design decision here beyond "match the existing `Services/` convention" — this is a mechanical cleanup, not a redesign. The only thing worth stating explicitly: the namespace changes from `Anela.Heblo.Application.Features.BackgroundJobs` to `Anela.Heblo.Application.Features.BackgroundJobs.Services`, consistent with `RecurringJobSeeder` and the other files already in that folder (all of which use the `.Services` namespace).

## Implementation Guidance

### Directory / Module Structure
- Move: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/RecurringJobStatusChecker.cs` → `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobStatusChecker.cs`.
- Change its namespace declaration to `Anela.Heblo.Application.Features.BackgroundJobs.Services`.
- No other files in the module move.

### Interfaces and Contracts
None affected. `IRecurringJobStatusChecker` stays in `Anela.Heblo.Domain.Features.BackgroundJobs`, unchanged. The class continues to implement it verbatim (constructor, `IsJobEnabledAsync` signature and body untouched).

### Data Flow
Unaffected — no runtime behavior, DI resolution outcome, or call path changes.

Confirmed reference points requiring an update, from direct inspection of the current tree:
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/BackgroundJobsModule.cs`: already has `using Anela.Heblo.Application.Features.BackgroundJobs.Services;` (needed today for `RecurringJobSeeder`) and its own namespace is the module root (`...BackgroundJobs`), so `services.AddScoped<IRecurringJobStatusChecker, RecurringJobStatusChecker>();` will resolve correctly post-move via that existing `using` — **no line changes expected here**, but re-verify it compiles after the move (per spec FR-1).
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobStatusCheckerTests.cs`: currently has `using Anela.Heblo.Application.Features.BackgroundJobs;` — this **must** change to `using Anela.Heblo.Application.Features.BackgroundJobs.Services;` since the test only references the concrete class name (`RecurringJobStatusChecker`), not the interface.
- All other repo-wide matches for `RecurringJobStatusChecker` (searched across `backend/`) reference only the Domain interface `IRecurringJobStatusChecker` (e.g. `PurchasePriceRecalculationJob.cs`, `InvoiceDqtJob.cs`, `LotStockReconciliationDqtJob.cs`, `ProductPairingDqtJob.cs`, and others) — these are unaffected since the interface's namespace and location do not change.
- No other file references the concrete `RecurringJobStatusChecker` type by name or via the old namespace.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Missed `using` update leaves a stale reference and breaks the build | Low | Only one consumer (`RecurringJobStatusCheckerTests.cs`) needs a `using` change; confirmed by direct grep of the concrete type name across `backend/`. Run `dotnet build` to catch anything missed. |
| Merge/rebase conflict with other in-flight work touching this module | Low | Module is small and this PR should be scoped to just the move; merge promptly. |

## Specification Amendments
None. The specification's FR-1 already correctly identifies the two files needing changes (the moved file itself and `RecurringJobStatusCheckerTests.cs`) and correctly predicts that `BackgroundJobsModule.cs` likely needs no `using` change — direct inspection of the current source confirms both of these are accurate as written.

## Prerequisites
None. This can be implemented directly with no preceding work.
