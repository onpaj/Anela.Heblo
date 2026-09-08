# Architecture Review: InvoiceImportStatisticsSourceAdapter — remove direct ApplicationDbContext dependency

## Skip Design: true

## Architectural Fit Assessment
This is a textbook Clean Architecture boundary fix, not a new feature. The codebase already has the correct pattern in place one file over: `InvoiceConsumptionSourceAdapter` (same directory, same module) implements a sibling Consumer-Owned Contract (`IInvoiceConsumptionSource`) by depending on `IIssuedInvoiceRepository` — never on `ApplicationDbContext` or `Anela.Heblo.Persistence` directly. `InvoiceImportStatisticsSourceAdapter` is the outlier; the fix is to make it converge on the same pattern, not to invent a new one.

The consumer-owned-contract shape here (`IInvoiceImportStatisticsSource` declared in `Anela.Heblo.Domain.Features.Analytics`, implemented in `Anela.Heblo.Application.Features.Invoices.Infrastructure`, wired in `InvoicesModule.cs`) is explicitly documented as a precedent-following pattern in the interface's own XML doc comment and in `docs/architecture/development_guidelines.md`'s "Cross-Module Communication Example". This review does not touch that contract or its wiring — only what sits behind it inside the Invoices module.

Confirmed by reading the actual code (not just the issue body):
- `InvoiceImportStatisticsSourceAdapter.cs` — currently 102 lines, constructor takes `ApplicationDbContext`, method body runs two `GroupBy`/`ToListAsync` LINQ queries directly plus a gap-filling loop.
- `IIssuedInvoiceRepository.cs` (Domain) — five existing methods, all EF-free at the interface level (returns `IssuedInvoice`, `IssuedInvoiceSyncStats`, `PaginatedResult<IssuedInvoice>`, etc.). Adding a sixth, `GetDailyCountsAsync`, is consistent with its existing shape (e.g. `GetSyncStatsAsync(DateTime fromDate, DateTime toDate, ...)` is an almost identical signature pattern already returning an aggregated projection).
- `IssuedInvoiceRepository.cs` (Persistence) — already has multiple `GroupBy` aggregation queries (`GetSyncStatsAsync` groups by a constant key to produce one aggregate row; the new method groups by day). No new patterns needed here either.
- `InvoicesModule.cs` and DI: `IIssuedInvoiceRepository -> IssuedInvoiceRepository` and `IInvoiceImportStatisticsSource -> InvoiceImportStatisticsSourceAdapter` are both already registered. No DI change required.
- `ModuleBoundariesTests.cs` enforces *cross-module* boundaries (e.g. `Analytics (Application) -> Invoices` forbids `Anela.Heblo.Persistence.Invoices` from the Analytics namespace) via namespace-prefix scanning — it does not currently assert a blanket "no Application namespace may reference `Anela.Heblo.Persistence`" rule, which is why this violation compiled cleanly and shipped. This review does not propose adding that generic rule (see Specification Amendments) — it is a larger investment (would need per-assembly scanning across all Application code, not just Analytics) and out of scope for a one-file fix.

## Proposed Architecture

### Component Overview

```
Before:
  Application.Features.Invoices.Infrastructure
    InvoiceImportStatisticsSourceAdapter --(EF LINQ)--> Persistence.ApplicationDbContext.IssuedInvoices
                                                          ^^^ Application -> Persistence (VIOLATION)

After:
  Application.Features.Invoices.Infrastructure
    InvoiceImportStatisticsSourceAdapter --(interface call)--> Domain.Features.Invoices.IIssuedInvoiceRepository
                                                                        ^
                                                                        | implements
                                                                Persistence.Invoices.IssuedInvoiceRepository --(EF LINQ)--> ApplicationDbContext.IssuedInvoices

  (mirrors the existing InvoiceConsumptionSourceAdapter -> IIssuedInvoiceRepository -> IssuedInvoiceRepository chain exactly)
```

### Key Design Decisions

#### Decision 1: Extend `IIssuedInvoiceRepository` rather than introduce a new narrow repository
**Options considered:**
1. Add `GetDailyCountsAsync` to the existing `IIssuedInvoiceRepository`.
2. Introduce a separate, narrower interface (e.g. `IInvoiceImportStatisticsRepository`) just for this query, to keep `IIssuedInvoiceRepository` from growing.

**Chosen approach:** Option 1 — extend `IIssuedInvoiceRepository`.

**Rationale:** `IIssuedInvoiceRepository` already carries several purpose-built query methods beyond plain CRUD (`GetSyncStatsAsync`, `GetPaginatedAsync`, `GetHeadersByDateAsync`, `GetByIdWithSyncHistoryAsync`) — it is already the module's general-purpose query surface for `IssuedInvoice`, not a narrow CRUD-only repository. `GetDailyCountsAsync` is one more read-shape on the same aggregate root and the same table; a new interface would be pure ceremony with no consumer needing to depend on a narrower surface (there is exactly one adapter using it, `InvoiceImportStatisticsSourceAdapter`). Introducing Interface Segregation here has no payoff and adds a file plus a DI registration for no reason.

#### Decision 2: Move the query verbatim, do not refactor the two-branch shape
**Options considered:**
1. Move the existing `if (dateType == ImportDateType.InvoiceDate) { ... } else { ... }` two-branch query as-is into the repository.
2. While moving it, unify the two branches into one parameterized `GroupBy` expression (e.g. select the grouping field via an `Expression<Func<IssuedInvoice, DateTime>>` chosen by `dateType`).

**Chosen approach:** Option 1 — move verbatim, no behavioral or structural change to the query itself.

**Rationale:** The issue is a boundary violation, not a code-quality complaint about the two-branch duplication. Keeping the diff behavior-preserving and minimal (per the spec's Out of Scope) makes the change trivially reviewable and safely testable by porting the existing adapter tests unchanged in intent. Unifying the branches is a legitimate future cleanup but is a separate concern with its own risk (LINQ-to-SQL translation of a dynamic group-by key is easy to get subtly wrong) — do not bundle it with an architecture fix.

#### Decision 3: Adapter becomes a pure pass-through
**Options considered:**
1. Adapter keeps only the interface-call delegation (one line), all date-kind handling and gap-filling logic moves to the repository.
2. Adapter keeps the UTC/Unspecified date normalization and gap-filling, repository only returns raw grouped rows.

**Chosen approach:** Option 1 — the repository owns the full query end-to-end (input validation/normalization, grouping, gap-filling) and returns the finished `IReadOnlyList<DailyInvoiceCount>`; the adapter becomes a one-line pass-through, exactly matching `InvoiceConsumptionSourceAdapter.GetHeadersByDateAsync`'s shape (call repository, adapt/return).

**Rationale:** Splitting "normalize dates" into the adapter and "query + gap-fill" into the repository would leave a partial, harder-to-test division of responsibility for no benefit — none of that logic depends on anything Persistence-specific except the final grouping, but all of it exists purely to serve that one query and has no other caller. Keeping it together in the repository method matches the precedent (`GetSyncStatsAsync` similarly owns its whole query-to-DTO pipeline in the repository) and keeps the adapter trivially thin, which is exactly what an "adapter satisfying a consumer-owned contract" should be.

## Implementation Guidance

### Directory / Module Structure
No new files or directories. Three existing files change:
- `backend/src/Anela.Heblo.Domain/Features/Invoices/IIssuedInvoiceRepository.cs` — add method signature + `using Anela.Heblo.Domain.Features.Analytics;`.
- `backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs` — add method implementation (place it near `GetSyncStatsAsync`, the most similar existing method, for readability).
- `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceImportStatisticsSourceAdapter.cs` — replace `ApplicationDbContext` dependency with `IIssuedInvoiceRepository`; method body becomes a one-line delegation.

Test files:
- `backend/test/Anela.Heblo.Tests/Features/Invoices/IssuedInvoiceRepositoryTests.cs` — this is the existing repository test fixture (already constructs `IssuedInvoiceRepository` against an in-memory `ApplicationDbContext` with a mocked `ILogger`). Add the moved `GetDailyCountsAsync` test cases here as new `[Fact]`s, following its existing constructor/setup pattern — do not create a new test file.
- `backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/InvoiceImportStatisticsSourceAdapterTests.cs` — rewrite to mock `IIssuedInvoiceRepository` (via `Moq`, already a project dependency — see `IssuedInvoiceRepositoryTests.cs`'s `Mock<ILogger<...>>` usage and `InvoiceConsumptionSourceAdapterTests.cs`) and assert simple pass-through: the adapter calls `_repository.GetDailyCountsAsync` with the same arguments it received and returns its result unchanged. No in-memory DbContext needed in this file after the change.

### Interfaces and Contracts

`IIssuedInvoiceRepository` (Domain, add after `GetHeadersByDateAsync`, before `RevertTrackedChangesAsync`, to sit next to the other read-query methods):

```csharp
Task<IReadOnlyList<DailyInvoiceCount>> GetDailyCountsAsync(
    DateTime startDate,
    DateTime endDate,
    ImportDateType dateType,
    CancellationToken cancellationToken = default);
```

`InvoiceImportStatisticsSourceAdapter` (Application) after the change:

```csharp
using Anela.Heblo.Domain.Features.Analytics;
using Anela.Heblo.Domain.Features.Invoices;

namespace Anela.Heblo.Application.Features.Invoices.Infrastructure;

internal sealed class InvoiceImportStatisticsSourceAdapter : IInvoiceImportStatisticsSource
{
    private readonly IIssuedInvoiceRepository _repository;

    public InvoiceImportStatisticsSourceAdapter(IIssuedInvoiceRepository repository)
    {
        _repository = repository;
    }

    public Task<IReadOnlyList<DailyInvoiceCount>> GetDailyCountsAsync(
        DateTime startDate,
        DateTime endDate,
        ImportDateType dateType,
        CancellationToken cancellationToken = default)
    {
        return _repository.GetDailyCountsAsync(startDate, endDate, dateType, cancellationToken);
    }
}
```

No `IInvoiceImportStatisticsSource` (the consumer-owned contract) change. No `InvoicesModule.cs` change — both `IIssuedInvoiceRepository` and `IInvoiceImportStatisticsSource` bindings already exist.

### Data Flow
`GetInvoiceImportStatisticsHandler` (Analytics, unchanged) calls `IInvoiceImportStatisticsSource.GetDailyCountsAsync` → resolves to `InvoiceImportStatisticsSourceAdapter` (Invoices, Application layer) → now calls `IIssuedInvoiceRepository.GetDailyCountsAsync` → resolves to `IssuedInvoiceRepository` (Invoices, Persistence layer) → runs the EF Core query against `ApplicationDbContext` → returns gap-filled `List<DailyInvoiceCount>` back up the chain unchanged. This is one additional interface hop compared to today, identical in shape to how `InvoiceConsumptionSourceAdapter` already calls through `IIssuedInvoiceRepository.GetHeadersByDateAsync`.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Moving the query changes its EF-to-SQL translation subtly (e.g. `DbSet` vs `_dbContext.IssuedInvoices` behave identically, but worth double-checking `DbSet` is exposed as `IssuedInvoice`-typed in `BaseRepository`) | Low | `DbSet` is already used by every other method in `IssuedInvoiceRepository` against the same `IssuedInvoice` entity; confirmed by reading `GetPaginatedAsync`/`GetHeadersByDateAsync` in the same file — no risk of a different queryable shape. |
| Test coverage gap during the move (old test file deleted/gutted before new coverage lands) | Medium | FR-4 requires the new/updated tests to land in the same change as the production code move — treat this as one atomic task, not two; do not merge the adapter/repository change without the test move landing in the same commit set. |
| A generic architecture test (`ModuleBoundariesTests`) does not exist to prevent regression of this exact violation in the future | Low | Explicitly out of scope per spec; noted here so it is not silently forgotten — worth a follow-up issue, not part of this fix. |

## Specification Amendments
None. The spec (`spec.r1.md`) already correctly scopes this as a minimal, behavior-preserving move with test relocation (FR-1–FR-4) and explicitly excludes query-shape unification and new architecture-test rules (Out of Scope) — this review concurs with and does not change that scoping. One clarification added here, not a spec change: place the moved repository tests in the existing `IssuedInvoiceRepositoryTests.cs` fixture (confirmed to exist and already follow the right in-memory-DbContext + mocked-logger pattern) rather than a new file, since the spec left the exact target file open ("if one exists").

## Prerequisites
None. No migration, no config, no infrastructure change — this is a same-commit, three-file production code change plus test relocation, buildable and testable in isolation.
