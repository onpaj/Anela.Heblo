# Architecture Review: Refactor BankStatementStatisticsSourceAdapter to Use IBankStatementImportRepository

## Skip Design: true

## Architectural Fit Assessment

This refactoring aligns exactly with the project's established layering rules. The violation is unambiguous: `BankStatementStatisticsSourceAdapter` lives in `Anela.Heblo.Application` but imports `Anela.Heblo.Persistence.ApplicationDbContext` and `Microsoft.EntityFrameworkCore`, punching two layers down. The Architecture Documentation and `development_guidelines.md` are explicit: Application-layer types must access the database only through Domain-layer repository interfaces implemented in the Persistence layer.

The integration points are already in place:

- `IBankStatementImportRepository` exists in `Anela.Heblo.Domain.Features.Bank` and is registered by `BankModule.cs` (`services.AddScoped<IBankStatementImportRepository, BankStatementImportRepository>()`).
- `BankStatementImportRepository` already holds `ApplicationDbContext` and follows the `AsNoTracking()` / EF Core LINQ pattern used by every other repository in the codebase.
- `DailyBankStatementStatistics` and `BankStatementDateType` already live in `Anela.Heblo.Domain.Features.Analytics`, which is a valid cross-namespace reference from `IBankStatementImportRepository` (both are Domain types).
- The DI lifetime is already correct: both `IBankStatementImportRepository` and `IBankStatementStatisticsSource` are registered as `Scoped`, which matches `ApplicationDbContext`.

The only risk worth noting: the existing unit tests (`BankStatementStatisticsSourceAdapterTests`) construct `BankStatementStatisticsSourceAdapter` directly with an `ApplicationDbContext` in-memory instance. After the refactor the constructor signature changes, so those tests must be updated to inject a mock or stub of `IBankStatementImportRepository` rather than a raw `ApplicationDbContext`.

## Proposed Architecture

### Component Overview

```
Anela.Heblo.Domain
└── Features
    ├── Bank
    │   └── IBankStatementImportRepository   ← ADD: GetDailyStatisticsAsync(...)
    └── Analytics
        ├── DailyBankStatementStatistics      (unchanged)
        ├── BankStatementDateType             (unchanged)
        └── IBankStatementStatisticsSource    (unchanged)

Anela.Heblo.Persistence
└── Features
    └── Bank
        └── BankStatementImportRepository    ← ADD: GetDailyStatisticsAsync implementation
                                               (extracted verbatim from adapter; EF Core stays here)

Anela.Heblo.Application
└── Features
    └── Bank
        ├── BankModule.cs                    (unchanged — IBankStatementImportRepository already registered)
        └── Infrastructure
            └── BankStatementStatisticsSourceAdapter
                                             ← CHANGE: replace ApplicationDbContext with
                                               IBankStatementImportRepository; delegate query,
                                               retain gap-fill loop

Anela.Heblo.Tests
└── Features
    └── Bank
        └── BankStatementStatisticsSourceAdapterTests
                                             ← UPDATE: constructor arg changes from
                                               ApplicationDbContext to IBankStatementImportRepository
```

Data flow (unchanged externally):

```
Analytics handler
  → IAnalyticsRepository (AnalyticsRepository, Persistence layer)
    → IBankStatementStatisticsSource.GetDailyStatisticsAsync
      → BankStatementStatisticsSourceAdapter (Application/Bank/Infrastructure)
        → IBankStatementImportRepository.GetDailyStatisticsAsync  [NEW]
          → BankStatementImportRepository (Persistence/Bank)
            → ApplicationDbContext.BankStatements [EF Core query, unchanged]
        ← IReadOnlyList<DailyBankStatementStatistics> (sparse, from DB)
      ← gap-fill loop executes in adapter (unchanged)
    ← IReadOnlyList<DailyBankStatementStatistics> (dense, gap-filled)
```

### Key Design Decisions

#### Decision 1: Where to put the UTC-to-Unspecified conversion

**Options considered:**
- Keep it in `BankStatementImportRepository.GetDailyStatisticsAsync` (caller passes UTC, repository normalises before querying).
- Move it to the caller (`BankStatementStatisticsSourceAdapter`) and pass already-normalised values into the repository.

**Chosen approach:** Keep it in the repository implementation, exactly as it lives today in the adapter.

**Rationale:** The conversion is a persistence concern — PostgreSQL stores `timestamp without time zone` and EF Core compares `DateTimeKind.Unspecified`. Callers must not need to know that. The repository is the correct owner of that knowledge. This matches how `GetFilteredAsync` in the same repository handles date comparisons via `.Date` projection.

#### Decision 2: Single method body vs. two overloads

**Options considered:**
- Two overloads: `GetDailyStatisticsForStatementDateAsync` / `GetDailyStatisticsForImportDateAsync`.
- Single method accepting `BankStatementDateType` with an internal switch.

**Chosen approach:** Single method with `BankStatementDateType` parameter, as specified.

**Rationale:** Both overloads would have identical signatures except for one field selector. The enum already exists and is a Domain type. A single method surfaces the distinction as data, not as API surface — consistent with how the existing `GetFilteredAsync` accepts a flexible filter object rather than per-field overloads. Avoids artificial interface growth.

#### Decision 3: Test strategy after the refactor

**Options considered:**
- Rewrite tests to use `ApplicationDbContext` in-memory and test through the repository, relying on the adapter delegating correctly.
- Rewrite tests to mock `IBankStatementImportRepository` and test the adapter's gap-fill logic in isolation; add separate repository-level tests.

**Chosen approach:** Mock `IBankStatementImportRepository` in the adapter tests; retain the existing gap-fill test cases. Add or migrate the DB-query tests to the repository directly (or keep them in the adapter test class via a real in-memory `BankStatementImportRepository` wrapping the in-memory `ApplicationDbContext`).

**Rationale:** The adapter's unit-testable behaviour is the gap-fill loop; the query logic belongs to the repository layer. Separating these concerns makes each test class smaller and its failure messages unambiguous. The simplest migration is to construct a real `BankStatementImportRepository` over the existing in-memory `ApplicationDbContext` and pass it to the adapter — no mocking framework needed, same seeding pattern.

## Implementation Guidance

### Directory / Module Structure

No new files are needed. Changes touch exactly three existing files plus the test file:

| File | Change |
|------|--------|
| `backend/src/Anela.Heblo.Domain/Features/Bank/IBankStatementImportRepository.cs` | Add `GetDailyStatisticsAsync` method signature and two `using` directives for `Anela.Heblo.Domain.Features.Analytics` types |
| `backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportRepository.cs` | Add `GetDailyStatisticsAsync` implementation; no other methods change |
| `backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/BankStatementStatisticsSourceAdapter.cs` | Replace `ApplicationDbContext` field/ctor with `IBankStatementImportRepository`; replace lines 22–77 with a single delegation call; retain lines 79–103 (gap-fill) unchanged |
| `backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementStatisticsSourceAdapterTests.cs` | Update constructor: instantiate `BankStatementImportRepository(_context)` and pass that to the adapter instead of `_context` directly |

`BankModule.cs` is unchanged. No migrations. No new DI registrations.

### Interfaces and Contracts

New method to add to `IBankStatementImportRepository`:

```csharp
using Anela.Heblo.Domain.Features.Analytics;   // add at top of file

Task<IReadOnlyList<DailyBankStatementStatistics>> GetDailyStatisticsAsync(
    DateTime startDate,
    DateTime endDate,
    BankStatementDateType dateType,
    CancellationToken cancellationToken = default);
```

Return type semantics (contract, not implementation detail):
- One row per calendar day in `[startDate.Date, endDate.Date]` where at least one `BankStatements` row exists.
- Days with no data are absent from the result (gap-filling is not the repository's job).
- `Date` is tagged `DateTimeKind.Utc`.
- Results are ordered ascending by `Date`.

### Data Flow

**Happy path — StatementDate branch:**

1. `BankStatementStatisticsSourceAdapter.GetDailyStatisticsAsync(start, end, StatementDate)` receives UTC `DateTime`s.
2. Calls `_repository.GetDailyStatisticsAsync(start, end, StatementDate, ct)`.
3. Repository converts to `DateTimeKind.Unspecified`, executes `WHERE StatementDate >= startUnspec AND StatementDate <= endUnspec`, groups by `(Year, Month, Day)` of `StatementDate`, projects into `DailyBankStatementStatistics` with `DateTimeKind.Utc`, orders ascending, returns.
4. Adapter receives sparse list, builds `resultsByDate` dictionary, runs gap-fill loop, returns dense list.

**ImportDate branch:** identical except repository switches date field selector; adapter code is the same.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Existing adapter tests fail to compile after constructor change | Low | Update `BankStatementStatisticsSourceAdapterTests` constructor to pass `new BankStatementImportRepository(_context)` — one-line change; all test cases and assertions remain valid |
| UTC-to-Unspecified logic duplicated if future callers of `GetDailyStatisticsAsync` do not pass UTC | Low | Document on the interface method that `startDate`/`endDate` must be UTC; the repository normalises internally. A `DateTimeKind.Unspecified` argument produces the same result as UTC in this context (kind is overwritten), so the risk is cosmetic. |
| EF Core in-memory provider skips some translation paths that PostgreSQL enforces | Low | Existing tests already use in-memory DB against the same LINQ expressions; the extraction does not change the query shape. Integration tests against staging cover real PostgreSQL. |
| `AsNoTracking()` missing in new method | Low | Mitigated by spec FR-2 explicitly requiring `AsNoTracking()`; cross-check in code review. |

## Specification Amendments

None required. The spec is complete and accurate. One clarification worth stating for implementors that is implicit in the spec but not spelled out:

The `using Anela.Heblo.Domain.Features.Analytics;` directive added to `IBankStatementImportRepository.cs` is a cross-namespace reference within the same assembly (`Anela.Heblo.Domain`). This is architecturally acceptable — both namespaces are Domain types and there is no layer violation. The spec acknowledges this correctly.

## Prerequisites

All prerequisites already exist:

- `IBankStatementImportRepository` and `BankStatementImportRepository` are present and registered.
- `DailyBankStatementStatistics` and `BankStatementDateType` are present in `Anela.Heblo.Domain.Features.Analytics`.
- `IBankStatementStatisticsSource` is defined and wired.
- No schema changes, no migrations, no new infrastructure.
