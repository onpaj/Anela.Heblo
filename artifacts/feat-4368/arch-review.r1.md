# Architecture Review: DqtJobBase extraction

## Skip Design: true
Backend-only refactor, no UI.

## Architectural Fit Assessment
Fits Vertical Slice layout: jobs live in `Features/DataQuality/Infrastructure/Jobs/`. Base class is local to the slice; no cross-module exposure. `IRecurringJob` discovery registers concrete classes, so an abstract base is not registered (verify no scan picks up abstract types; abstract types cannot be instantiated and typical scanners skip them).

## Proposed Architecture

### Component Overview
`IRecurringJob` <- `DqtJobBase` (abstract: ExecuteAsync template) <- 5 concrete jobs (Metadata, TestType, GetTargetDate, RunAsync -> injected runner).

### Key Design Decisions
#### Decision 1: Template-method base class vs. helper/composition
**Options considered:** abstract base (issue's suggestion); shared helper service.
**Chosen approach:** abstract base, as the issue suggests; smallest change, constructor signatures of concrete classes preserved.
**Rationale:** only variation is data plus one delegated call.

#### Decision 2: Logger
Base ctor takes non-generic `ILogger`; concrete classes pass their `ILogger<T>` to preserve categories.

#### Decision 3: Date rule
`protected abstract DateOnly GetTargetDate(DateOnly today)`; Invoice/StockWriteBack return `today.AddDays(-1)`, others `today`.

## Implementation Guidance
### Directory / Module Structure
New: `backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/DqtJobBase.cs`. Modify the five job files. Add `backend/test/Anela.Heblo.Tests/Features/DataQuality/PriceComparisonDqtJobTests.cs` modelled on `ProductPairingDqtJobTests.cs`.
### Interfaces and Contracts
`public abstract RecurringJobMetadata Metadata { get; }` (concrete classes keep `public RecurringJobMetadata Metadata { get; } = new() {...}` via `override`); keep `ExecuteAsync(CancellationToken cancellationToken = default)` signature. Beware: Metadata is used in base `ExecuteAsync` only at runtime, not in the ctor, so initializer ordering is safe.
### Data Flow
Unchanged: enabled check -> date -> log -> DqtRun.Start -> persist -> RunAsync.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Log category change | Low | pass `ILogger<T>` to base |
| Abstract type picked up by job registration | Low | check registration code and DataQualityModuleTests |
| Subtle behaviour drift (date, log text) | Low | existing job tests + new PriceComparison test |

## Specification Amendments
None.

## Prerequisites
None. Run `dotnet build`, `dotnet format`, and DataQuality tests; bump `verified_at` in affected `docs/processes/calc-dqt-*.md` / `module-data-quality.md` and run `python3 scripts/process-docs/check.py index` if CI's process-doc gate requires it.
