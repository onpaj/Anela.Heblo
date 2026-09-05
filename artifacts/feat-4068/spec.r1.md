# Specification: [arch-review] Invoices: InvoiceImportStatisticsSourceAdapter directly injects ApplicationDbContext, violating Application→Infrastructure boundary

## Summary
`InvoiceImportStatisticsSourceAdapter` (in the Application layer, `Anela.Heblo.Application.Features.Invoices.Infrastructure`) injects `ApplicationDbContext` directly instead of going through the domain repository abstraction `IIssuedInvoiceRepository`, violating the documented Clean Architecture rule that the Application layer must not depend on Persistence. This fix moves the adapter's EF Core grouping query into `IssuedInvoiceRepository` (Persistence), exposes it through a new `IIssuedInvoiceRepository` method (Domain), and rewires the adapter to use that interface — bringing it in line with the sibling `InvoiceConsumptionSourceAdapter`, which already follows this pattern.

## Background
`InvoiceImportStatisticsSourceAdapter` implements `IInvoiceImportStatisticsSource`, a Consumer-Owned Contract owned by the Analytics module (`Anela.Heblo.Domain.Features.Analytics.IInvoiceImportStatisticsSource`) and consumed by `GetInvoiceImportStatisticsHandler` to power the invoice-import dashboard tile. The adapter currently takes a constructor dependency on `Anela.Heblo.Persistence.ApplicationDbContext` and runs two EF Core `GroupBy`/`ToListAsync` queries directly against `_dbContext.IssuedInvoices`.

Per `docs/architecture/development_guidelines.md`, Application code must depend only on Domain abstractions (repository interfaces), never directly on `Anela.Heblo.Persistence`. Every other adapter in the Invoices module already follows this: `InvoiceConsumptionSourceAdapter` takes `IIssuedInvoiceRepository`, not `ApplicationDbContext`. `InvoiceImportStatisticsSourceAdapter` is the sole exception, discovered by the daily arch-review routine on 2026-09-04 (see `docs/architecture/📘 Architecture Documentation – MVP Work.md` and `development_guidelines.md` for the governing rules).

This is a pure internal refactor: no user-visible behavior, API contract, or DI wiring registration changes. `InvoicesModule.cs` already registers `IInvoiceImportStatisticsSource -> InvoiceImportStatisticsSourceAdapter` and `IIssuedInvoiceRepository -> IssuedInvoiceRepository`; neither registration needs to change.

## Functional Requirements

### FR-1: Add a repository method for grouped daily invoice counts
Add a new method to `IIssuedInvoiceRepository` (Domain layer, `backend/src/Anela.Heblo.Domain/Features/Invoices/IIssuedInvoiceRepository.cs`):

```csharp
Task<IReadOnlyList<DailyInvoiceCount>> GetDailyCountsAsync(
    DateTime startDate,
    DateTime endDate,
    ImportDateType dateType,
    CancellationToken cancellationToken = default);
```

`DailyInvoiceCount` and `ImportDateType` live in `Anela.Heblo.Domain.Features.Analytics`. `IIssuedInvoiceRepository` lives in `Anela.Heblo.Domain.Features.Invoices` — the Invoices domain interface referencing an Analytics domain type is an existing, accepted pattern in this codebase (`IInvoiceImportStatisticsSource` already sits in Analytics and is implemented from Invoices; both are Domain-layer types, so this is a Domain→Domain reference, not a layer violation). Add a `using Anela.Heblo.Domain.Features.Analytics;` to the interface file.

**Acceptance criteria:**
- `IIssuedInvoiceRepository` declares `GetDailyCountsAsync` with the exact signature above.
- The method's XML doc (or a short comment) states it returns UTC-dated, gap-filled daily counts, matching the existing contract on `IInvoiceImportStatisticsSource.GetDailyCountsAsync`.

### FR-2: Implement the query in `IssuedInvoiceRepository`
Move the existing EF Core query logic (both the `InvoiceDate` branch and the `LastSyncTime` branch, including the `DateTime.SpecifyKind` UTC/Unspecified handling and the gap-filling loop that produces zero-count rows for missing dates) from `InvoiceImportStatisticsSourceAdapter.GetDailyCountsAsync` into `IssuedInvoiceRepository.GetDailyCountsAsync` (`backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs`), operating on `DbSet` instead of `_dbContext.IssuedInvoices`.

Preserve behavior exactly:
- UTC-to-Unspecified conversion of `startDate`/`endDate` before querying (EF Core / Npgsql compares against `Unspecified`-kind stored timestamps).
- Grouping by day for whichever field `dateType` selects (`InvoiceDate` vs `LastSyncTime`), including the `LastSyncTime.HasValue` filter for the `LastSyncTime` branch.
- Ordering by date ascending.
- Gap-filling every day in `[startDate.Date, endDate.Date]` inclusive with `Count = 0` where no row exists, using UTC-kinded `DateTime` values in the result.

**Acceptance criteria:**
- `IssuedInvoiceRepository.GetDailyCountsAsync` produces identical output to the current adapter implementation for the same inputs (verified by moving/adapting the existing adapter unit tests — see FR-4).
- No EF Core query logic referencing `IssuedInvoices`/`DbSet` remains in the Application layer after this change.

### FR-3: Rewire the adapter to use the repository interface
Change `InvoiceImportStatisticsSourceAdapter` to:
- Remove the `using Anela.Heblo.Persistence;` import and the `Microsoft.EntityFrameworkCore` import (no longer needed once the query moves out).
- Add `using Anela.Heblo.Domain.Features.Invoices;`.
- Replace the `ApplicationDbContext _dbContext` field and constructor parameter with `IIssuedInvoiceRepository _repository`.
- Replace the method body with a single delegating call: `return await _repository.GetDailyCountsAsync(startDate, endDate, dateType, cancellationToken);` (or return the `Task` directly without `await` if preferred — match existing code style of the sibling adapter, which uses `await`).

**Acceptance criteria:**
- `InvoiceImportStatisticsSourceAdapter.cs` no longer references `Anela.Heblo.Persistence` or `ApplicationDbContext` anywhere.
- The adapter's public method signature (`GetDailyCountsAsync` on `IInvoiceImportStatisticsSource`) is unchanged — no consumer-facing change.
- `dotnet build` succeeds with no new compiler warnings introduced by this change.

### FR-4: Update/relocate existing tests
`backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/InvoiceImportStatisticsSourceAdapterTests.cs` currently constructs the adapter directly with an in-memory `ApplicationDbContext` and exercises the grouping/gap-fill logic through it. After this change the adapter no longer accepts `ApplicationDbContext`, so:
- The grouping/gap-fill behavioral test coverage must move to a new or existing `IssuedInvoiceRepository` test fixture (e.g. `backend/test/Anela.Heblo.Tests/Persistence/Invoices/IssuedInvoiceRepositoryTests.cs` if one exists, or a new file colocated with other `IssuedInvoiceRepository` tests) that constructs the repository against an in-memory `ApplicationDbContext` and asserts the same cases (`InvoiceDate` branch, `LastSyncTime` branch, gap-filling for missing days, UTC date-kind on results).
- `InvoiceImportStatisticsSourceAdapterTests.cs` should be simplified to a thin test that mocks `IIssuedInvoiceRepository` (matching the style of `InvoiceConsumptionSourceAdapterTests.cs`) and asserts the adapter passes its arguments through and returns the repository's result unchanged.

**Acceptance criteria:**
- All existing test cases in `InvoiceImportStatisticsSourceAdapterTests.cs` (both `dateType` branches, gap-filling for missing dates) have an equivalent assertion after the move — no test coverage is lost.
- New/updated tests pass under `dotnet test`.
- The adapter-level test file no longer touches `ApplicationDbContext`.

## Non-Functional Requirements

### NFR-1: No behavior change
This is a structural refactor only. Query results, date handling (UTC/Unspecified kind conversions), and gap-filling semantics must be bit-for-bit identical to the current implementation. No new caching, no changed grouping granularity, no changed inclusive/exclusive date-range semantics.

### NFR-2: No performance regression
The query moves from Application to Persistence unchanged — same EF Core LINQ, same indexes used (none added or removed by this change), same round-trip count (still two separate `ToListAsync` calls, one per branch, as today — this task does not attempt to unify or optimize the two branches).

## Data Model
No data model or schema changes. No new entities, no migration. `DailyInvoiceCount` (Domain/Analytics) and `ImportDateType` (Domain/Analytics) are reused as-is.

## API / Interface Design
- **Domain**: `IIssuedInvoiceRepository` gains one new method, `GetDailyCountsAsync` (FR-1).
- **Persistence**: `IssuedInvoiceRepository` gains the corresponding implementation (FR-2).
- **Application**: `InvoiceImportStatisticsSourceAdapter` changes its constructor dependency from `ApplicationDbContext` to `IIssuedInvoiceRepository` and its method body becomes a pass-through delegation (FR-3).
- **No changes** to `IInvoiceImportStatisticsSource` (Domain/Analytics — the consumer-owned contract), `GetInvoiceImportStatisticsHandler`, `GetInvoiceImportStatisticsRequest/Response`, `InvoiceImportStatisticsTile`, or `InvoicesModule.cs` DI registrations. This is entirely internal to the Invoices module's adapter/repository boundary.

## Dependencies
- None external. Depends only on existing types already in the codebase (`IIssuedInvoiceRepository`, `IssuedInvoiceRepository`, `DailyInvoiceCount`, `ImportDateType`, `ApplicationDbContext`).
- No feature flag needed (internal refactor, not user-facing).

## Out of Scope
- Any change to the two-branch (`InvoiceDate` vs `LastSyncTime`) query shape, e.g. unifying them into one parameterized query — left as-is to keep this a minimal, behavior-preserving refactor.
- Any change to `IInvoiceImportStatisticsSource`, the Analytics dashboard tile, or the `GetInvoiceImportStatistics` use case.
- Broader architecture-test coverage (e.g., adding a `ModuleBoundariesTests` rule that would generically forbid `Anela.Heblo.Application.Features.Invoices.Infrastructure -> Anela.Heblo.Persistence` at the assembly level) — the existing `ModuleBoundariesTests` rules enforce cross-*module* boundaries (e.g., Analytics -> Invoices), not this general Application-layer-to-Persistence-layer rule; adding such a rule is a separate, larger architectural investment and not required to fix this specific finding.
- Any change to `IssuedInvoiceRepository`'s other methods (`GetSyncStatsAsync`, `GetPaginatedAsync`, etc.).

## Open Questions
None.

## Status: COMPLETE
