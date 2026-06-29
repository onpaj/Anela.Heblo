# Specification: Refactor BankStatementStatisticsSourceAdapter to Use IBankStatementImportRepository

## Summary

`BankStatementStatisticsSourceAdapter` currently injects `ApplicationDbContext` directly from the Persistence layer and executes EF Core queries inline, violating the project's repository abstraction pattern. This refactoring moves the two near-identical database query blocks into a new `GetDailyStatisticsAsync` method on `IBankStatementImportRepository` / `BankStatementImportRepository`, so the adapter depends only on the Domain interface and contains no EF Core references.

## Background

The architecture mandates that all database access in the Application layer goes through repository interfaces defined in the Domain layer and implemented in the Persistence layer. `BankStatementStatisticsSourceAdapter` (Application layer) breaks this rule by holding an `ApplicationDbContext` (Persistence layer) directly, leaking an EF Core dependency upward. The two LINQ blocks — one for `BankStatementDateType.StatementDate` and one for `BankStatementDateType.ImportDate` — are structurally identical except for the date field they filter and group on, creating a maintenance hazard: any schema change to `BankStatements` must be applied twice. The gap-filling loop that follows is pure in-memory logic and is correctly placed in the adapter; it stays there.

## Functional Requirements

### FR-1: New repository method — `GetDailyStatisticsAsync`

Add `GetDailyStatisticsAsync` to `IBankStatementImportRepository` in `backend/src/Anela.Heblo.Domain/Features/Bank/IBankStatementImportRepository.cs`.

Signature:

```csharp
Task<IReadOnlyList<DailyBankStatementStatistics>> GetDailyStatisticsAsync(
    DateTime startDate,
    DateTime endDate,
    BankStatementDateType dateType,
    CancellationToken cancellationToken = default);
```

The method returns one `DailyBankStatementStatistics` row per calendar day in the inclusive range `[startDate.Date, endDate.Date]` where at least one `BankStatements` row exists. Days with no rows are **not** gap-filled here — that responsibility stays in the adapter (FR-3).

**Acceptance criteria:**
- The interface compiles with the new method and no other interface members are changed.
- The return type `DailyBankStatementStatistics` is already in `Anela.Heblo.Domain.Features.Analytics`; the interface may reference it via a `using` directive (cross-namespace within Domain is acceptable; no Persistence or Application references are introduced).
- `BankStatementDateType` is in `Anela.Heblo.Domain.Features.Analytics`; same rule applies.

### FR-2: Repository implementation in Persistence layer

Implement `GetDailyStatisticsAsync` in `BankStatementImportRepository` (`backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportRepository.cs`).

The implementation is extracted verbatim from the two branches currently in `BankStatementStatisticsSourceAdapter` lines 32–77, unified into a single method body that switches on `dateType`:

- Apply UTC-to-Unspecified conversion for `startDate` and `endDate` before querying (matching the existing adapter logic at lines 22–28).
- When `dateType == BankStatementDateType.StatementDate`: filter on `b.StatementDate`, group by `(Year, Month, Day)` of `StatementDate`.
- When `dateType == BankStatementDateType.ImportDate`: filter on `b.ImportDate`, group by `(Year, Month, Day)` of `ImportDate`.
- Project into `DailyBankStatementStatistics` with `Date` tagged `DateTimeKind.Utc`.
- Use `AsNoTracking()` (read-only query).
- Order results ascending by date before returning.

**Acceptance criteria:**
- `dotnet build` passes with no errors or new warnings.
- The implementation contains no duplicate query blocks; the two branches are distinguished only by the date-field selector.
- No new EF Core migration is required (no schema changes).

### FR-3: Refactor `BankStatementStatisticsSourceAdapter` to inject `IBankStatementImportRepository`

Replace the `ApplicationDbContext` dependency in `BankStatementStatisticsSourceAdapter` (`backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/BankStatementStatisticsSourceAdapter.cs`) with `IBankStatementImportRepository`.

Changes:
- Remove the `using Anela.Heblo.Persistence;` and `using Microsoft.EntityFrameworkCore;` directives.
- Replace the constructor parameter and backing field.
- Replace the body of `GetDailyStatisticsAsync` (lines 22–77) with a single call to `_repository.GetDailyStatisticsAsync(startDate, endDate, dateType, cancellationToken)`.
- Retain the gap-filling loop (lines 79–103) unchanged.

**Acceptance criteria:**
- The file contains no reference to `ApplicationDbContext`, `Anela.Heblo.Persistence`, or `Microsoft.EntityFrameworkCore`.
- The adapter still implements `IBankStatementStatisticsSource` and returns the same gap-filled `IReadOnlyList<DailyBankStatementStatistics>` as before.
- `dotnet build` passes.

### FR-4: DI registration unchanged

The adapter is registered via `BankModule` (or equivalent DI setup). No DI registration changes are needed because `IBankStatementImportRepository` is already registered; the swap is purely at the constructor level.

**Acceptance criteria:**
- No changes to any DI/module registration file are required (verify by confirming `IBankStatementImportRepository` is already wired to `BankStatementImportRepository`).
- The application starts without DI resolution errors.

## Non-Functional Requirements

### NFR-1: Performance

The extracted queries must be functionally identical to the originals. No additional round-trips or in-memory materialisation steps are introduced. `AsNoTracking()` must be used.

### NFR-2: Correctness / backward compatibility

The observable output of `IBankStatementStatisticsSource.GetDailyStatisticsAsync` must be byte-for-byte identical to the current output for the same inputs. The refactoring is purely structural; no business logic changes are permitted.

### NFR-3: Layer integrity

After this change:
- `Anela.Heblo.Application` must have zero direct references to `Anela.Heblo.Persistence` or EF Core in `BankStatementStatisticsSourceAdapter`.
- All EF Core LINQ against `BankStatements` for statistics is consolidated in `Anela.Heblo.Persistence`.

## Data Model

No data model changes. The affected entity is `BankStatementImport` (table `BankStatements`) with the existing fields `StatementDate` (DateTime), `ImportDate` (DateTime), and `ItemCount` (int). No migration is needed.

## API / Interface Design

### `IBankStatementImportRepository` (Domain)

Add one method:

```csharp
Task<IReadOnlyList<DailyBankStatementStatistics>> GetDailyStatisticsAsync(
    DateTime startDate,
    DateTime endDate,
    BankStatementDateType dateType,
    CancellationToken cancellationToken = default);
```

No other interface changes.

### `BankStatementImportRepository` (Persistence)

Implement the method above. The two existing EF Core query blocks from the adapter move here verbatim, consolidated into one method.

### `BankStatementStatisticsSourceAdapter` (Application)

The adapter's `GetDailyStatisticsAsync` implementation shrinks to:
1. One repository call.
2. The existing gap-filling loop (unchanged).

No public API surface changes — `IBankStatementStatisticsSource` is unmodified.

## Dependencies

- `DailyBankStatementStatistics` — already in `Anela.Heblo.Domain.Features.Analytics`.
- `BankStatementDateType` — already in `Anela.Heblo.Domain.Features.Analytics`.
- `IBankStatementImportRepository` — already in `Anela.Heblo.Domain.Features.Bank`.
- `BankStatementImportRepository` — already in `Anela.Heblo.Persistence.Features.Bank`.
- No new NuGet packages or external services required.

## Out of Scope

- Changes to any caller of `IBankStatementStatisticsSource` or `IBankStatementImportRepository`.
- Changes to the `DailyBankStatementStatistics`, `BankStatementDateType`, or `IBankStatementStatisticsSource` contracts.
- Frontend changes.
- Performance optimisation of the underlying SQL (e.g. adding indexes) — out of scope for this refactoring ticket.
- Unit or integration tests (not explicitly requested; the change is a pure structural refactoring with no logic change).

## Open Questions

None.

## Status: COMPLETE
