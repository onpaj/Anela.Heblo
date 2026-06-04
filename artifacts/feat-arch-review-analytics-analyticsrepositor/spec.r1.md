# Specification: Decouple AnalyticsRepository from Invoices and Bank Modules

## Summary
Refactor `AnalyticsRepository` so it no longer queries `IssuedInvoices` and `BankStatements` directly via the shared `ApplicationDbContext`. Introduce two Consumer-Owned Contract interfaces in the Analytics module (`IInvoiceImportStatisticsSource`, `IBankStatementStatisticsSource`), implemented by adapters owned and registered by the Invoices and Bank modules respectively, mirroring the existing `IAnalyticsProductSource` / `CatalogAnalyticsSourceAdapter` pattern.

## Background
`AnalyticsRepository` currently violates the Clean Architecture / Vertical Slice module boundaries codified in `docs/architecture/development_guidelines.md`. Two of its methods reach across modules:

- **Invoice import statistics** (`AnalyticsRepository.cs` lines 68–97) query `_dbContext.IssuedInvoices` directly. `IssuedInvoice` is owned by the Invoices module (`Domain/Features/Invoices/`).
- **Bank statement statistics** (`AnalyticsRepository.cs` lines 164–199) query `_dbContext.BankStatements` directly. `BankStatementImport` is owned by the Bank module (`Domain/Features/Bank/`).

This tight coupling has two consequences:

1. **Schema-change blast radius:** Any change to `IssuedInvoice` or `BankStatementImport` (column rename, type change, relationship adjustment) propagates directly into Analytics persistence code, even though Analytics has no semantic ownership of those entities.
2. **Phase 2 blocker:** The roadmap for per-module `DbContext` instances cannot proceed while Analytics shares a context with Invoices and Bank. Once `IssuedInvoice` and `BankStatementImport` move to module-specific contexts, the existing queries break compilation.

The established pattern for solving this is already in the codebase. `IAnalyticsProductSource` lives in `Domain/Features/Analytics/`, declared as a Consumer-Owned Contract by Analytics. `CatalogAnalyticsSourceAdapter` (in `Application/Features/Catalog/Infrastructure/`) implements it and is registered in `CatalogModule` (`services.AddTransient<IAnalyticsProductSource, CatalogAnalyticsSourceAdapter>()`). Analytics depends only on the interface; Catalog is responsible for fulfilling it. The same inversion must be applied to invoice and bank-statement statistics.

A recent precedent confirms the direction: commit `c296d768` (`feat: Decouple Manufacture from ICatalogRepository via Consumer-Owned Contract`) applied the same pattern to remove a cross-module direct dependency.

The Invoices module already exposes an internal repository abstraction at `Application/Features/Invoices/Contracts/IIssuedInvoiceRepository.cs`; the Bank module exposes `Domain/Features/Bank/IBankStatementImportRepository.cs`. These existing abstractions are the natural data-access dependencies for the new adapters (each adapter stays inside its own module and uses its own module's repository, falling back to `ApplicationDbContext` only if a repository method is missing).

The enums `ImportDateType` and `BankStatementDateType` already live in `Domain/Features/Analytics/`, so passing them through Consumer-Owned Contracts implemented by Invoices/Bank introduces no circular dependency — those modules can already reference Analytics enums (and the new adapters will reference them by virtue of implementing Analytics-owned interfaces).

## Functional Requirements

### FR-1: Introduce `IInvoiceImportStatisticsSource` consumer contract
Add an interface in `Domain/Features/Analytics/` that exposes only the aggregated read shape Analytics needs from the Invoices module — no direct entity exposure.

**Acceptance criteria:**
- Interface file lives at `backend/src/Anela.Heblo.Domain/Features/Analytics/IInvoiceImportStatisticsSource.cs`.
- Interface declares exactly one method:
  ```csharp
  Task<IReadOnlyList<DailyInvoiceCount>> GetDailyCountsAsync(
      DateTime startDate,
      DateTime endDate,
      ImportDateType dateType,
      CancellationToken cancellationToken = default);
  ```
- The interface signature references **only** Analytics-owned types (`DailyInvoiceCount`, `ImportDateType`) and BCL types. No reference to `IssuedInvoice`, `ApplicationDbContext`, or any Invoices entity / DTO.
- XML doc comment on the interface states it is a Consumer-Owned Contract owned by the Analytics module, with the implementing module responsible for the data source. Reference `IAnalyticsProductSource` in the doc as the pattern precedent.
- `DailyInvoiceCount` keeps its current location (`Domain/Features/Analytics/DailyInvoiceCount.cs`) and public shape.

### FR-2: Introduce `IBankStatementStatisticsSource` consumer contract
Add the analogous interface for bank statement statistics.

**Acceptance criteria:**
- Interface file lives at `backend/src/Anela.Heblo.Domain/Features/Analytics/IBankStatementStatisticsSource.cs`.
- Interface declares exactly one method:
  ```csharp
  Task<IReadOnlyList<DailyBankStatementStatistics>> GetDailyStatisticsAsync(
      DateTime startDate,
      DateTime endDate,
      BankStatementDateType dateType,
      CancellationToken cancellationToken = default);
  ```
- The interface signature references **only** Analytics-owned types (`DailyBankStatementStatistics`, `BankStatementDateType`) and BCL types. No reference to `BankStatementImport`, `ApplicationDbContext`, or any Bank entity / DTO.
- XML doc comment on the interface states it is a Consumer-Owned Contract owned by the Analytics module.
- `DailyBankStatementStatistics` keeps its current location (`Domain/Features/Analytics/DailyBankStatementStatistics.cs`) and public shape.

### FR-3: Implement Invoices adapter
Provide an adapter in the Invoices module that fulfills `IInvoiceImportStatisticsSource`.

**Acceptance criteria:**
- Adapter file lives at `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceImportStatisticsSourceAdapter.cs`.
- Class is declared `internal sealed` (matches `CatalogAnalyticsSourceAdapter` precedent).
- Constructor injects the Invoices module's own data access — prefer `IIssuedInvoiceRepository` (already at `Application/Features/Invoices/Contracts/IIssuedInvoiceRepository.cs`). If that repository does not currently expose a query method capable of returning the needed `IQueryable<IssuedInvoice>` or aggregated rows, **either** add the smallest necessary method to `IIssuedInvoiceRepository` and its implementation, **or** fall back to `ApplicationDbContext` while keeping the adapter inside the Invoices module. Document the choice in the PR description.
- Adapter reproduces the **exact** aggregation behavior currently in `AnalyticsRepository.cs` lines 47–139, including:
  - PostgreSQL `DateTimeKind` normalization (input → UTC → `Unspecified` for `timestamp without time zone` queries).
  - `ImportDateType.InvoiceDate` branch: filter on `InvoiceDate`, group by year/month/day, count, order ascending.
  - `ImportDateType.SyncTime` (else) branch: filter on `LastSyncTime` (non-null), group by year/month/day of `LastSyncTime.Value`, count, order ascending.
  - Output projection into `DailyInvoiceCount` with `Date` re-tagged as `DateTimeKind.Utc` and `IsBelowThreshold = false`.
  - Date-range gap-filling (zero-count rows for missing dates) is implemented in the adapter, matching today's output exactly. The gap-filling loop currently in `AnalyticsRepository` (lines 113–138) moves into the adapter.
- Registered in `InvoicesModule.AddInvoicesModule` as:
  ```csharp
  services.AddTransient<IInvoiceImportStatisticsSource, InvoiceImportStatisticsSourceAdapter>();
  ```
  Lifetime matches `IAnalyticsProductSource` (transient). DI registration is done by the Invoices module, **never** by the Analytics module and **never** by `Persistence` infrastructure.

### FR-4: Implement Bank adapter
Provide the analogous adapter in the Bank module.

**Acceptance criteria:**
- Adapter file lives at `backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/BankStatementStatisticsSourceAdapter.cs`.
- Class is declared `internal sealed`.
- Constructor injects the Bank module's own data access — prefer `IBankStatementImportRepository` (already at `Domain/Features/Bank/IBankStatementImportRepository.cs`). Apply the same "extend repository or fall back to `ApplicationDbContext` inside the Bank module" rule as FR-3, documented in the PR description.
- Adapter reproduces the **exact** aggregation behavior currently in `AnalyticsRepository.cs` lines 144–236, including:
  - PostgreSQL `DateTimeKind` normalization identical to the invoice adapter.
  - `BankStatementDateType.StatementDate` branch: filter on `StatementDate`, group by year/month/day, project `ImportCount = Count()` and `TotalItemCount = Sum(ItemCount)`, order ascending.
  - Other (else, `ImportDate`) branch: filter on `ImportDate`, same grouping and projection.
  - Output projection into `DailyBankStatementStatistics` with `Date` re-tagged as `DateTimeKind.Utc`.
  - Gap-filling for missing dates with zero counts, matching today's output exactly. The gap-filling loop currently in `AnalyticsRepository` (lines 210–235) moves into the adapter.
- Registered in `BankModule.AddBankModule` as:
  ```csharp
  services.AddTransient<IBankStatementStatisticsSource, BankStatementStatisticsSourceAdapter>();
  ```
  Lifetime matches the invoice adapter precedent.

### FR-5: Replace direct queries in `AnalyticsRepository`
Remove the cross-module EF access from `AnalyticsRepository` and route the two operations through the new interfaces.

**Acceptance criteria:**
- `AnalyticsRepository` constructor accepts `IInvoiceImportStatisticsSource` and `IBankStatementStatisticsSource` in addition to its existing `IAnalyticsProductSource` and `ApplicationDbContext` dependencies.
- `GetInvoiceImportStatisticsAsync` becomes a single delegation: `return await _invoiceImportStatisticsSource.GetDailyCountsAsync(startDate, endDate, dateType, cancellationToken);` (returns `List<DailyInvoiceCount>` to preserve the `IAnalyticsRepository` interface signature — call `.ToList()` on the `IReadOnlyList<>` if and only if the interface return type cannot be widened to `IReadOnlyList<>` without breaking callers; otherwise widen the interface).
- `GetBankStatementImportStatisticsAsync` becomes a single delegation: `return await _bankStatementStatisticsSource.GetDailyStatisticsAsync(startDate, endDate, dateType, cancellationToken);` (same `List<>` vs `IReadOnlyList<>` rule).
- `AnalyticsRepository.cs` contains zero references to `_dbContext.IssuedInvoices` or `_dbContext.BankStatements` after the change.
- `using Anela.Heblo.Domain.Features.Invoices;` and `using Anela.Heblo.Domain.Features.Bank;` directives are removed from `AnalyticsRepository.cs` unless still needed for a non-entity reference (flag if so).
- `ApplicationDbContext` remains in the constructor **only** if `StreamProductsWithSalesAsync` / `GetProductAnalysisDataAsync` or other Analytics-owned operations still use it. If the field becomes unused after this refactor, remove it.
- DI registration of `AnalyticsRepository` (registration site, lifetime) is unchanged; only its constructor signature changes.

### FR-6: Behavior-preserving refactor
This is a refactor with no functional change. All callers of `AnalyticsRepository` must observe identical inputs and outputs.

**Acceptance criteria:**
- Existing unit and integration tests for invoice import statistics and bank statement statistics pass without modification of their assertions. Test setup may need to register the new adapters or fakes.
- For any test that previously asserted against in-memory `IssuedInvoices` or `BankStatements` seed data via `AnalyticsRepository`, the test continues to pass either by:
  - Registering the real adapters against the same in-memory `ApplicationDbContext` (preferred — matches today's behavior end-to-end), or
  - Providing fake `IInvoiceImportStatisticsSource` / `IBankStatementStatisticsSource` implementations in the test.
- No SQL query-shape regressions: the generated SQL for the invoice and bank statistics queries is functionally equivalent (same predicates, same `GROUP BY`, same aggregates). Verify by enabling EF query logging on one representative test per adapter and comparing the logged SQL against today's output.
- Gap-filling behavior is byte-for-byte identical to today (same dates emitted, same `DateTimeKind.Utc` tagging, same default zero-count rows).

### FR-7: Test coverage for new adapters
Each new adapter ships with focused tests.

**Acceptance criteria:**
- Tests for `InvoiceImportStatisticsSourceAdapter` cover:
  - `ImportDateType.InvoiceDate` branch — returns expected daily counts for seeded invoices.
  - `ImportDateType.SyncTime` branch — returns expected daily counts based on `LastSyncTime`, ignoring rows where `LastSyncTime` is null.
  - Empty result set returns the full range of zero-count rows (gap-filling verified).
  - Date range boundary inclusivity at both `startDate` and `endDate`.
- Tests for `BankStatementStatisticsSourceAdapter` cover:
  - `BankStatementDateType.StatementDate` branch — returns expected daily counts and summed `ItemCount`.
  - `BankStatementDateType.ImportDate` (other) branch — same.
  - Empty result set returns the full range of zero-count rows.
  - Date range boundary inclusivity at both `startDate` and `endDate`.
  - `TotalItemCount` correctly sums `ItemCount` across multiple imports on the same day.
- Tests live in the same test project / folder as the existing Invoices and Bank module tests (mirror existing conventions).
- The full backend test suite passes (`dotnet test`).

## Non-Functional Requirements

### NFR-1: Performance
The refactor must not regress query performance. Aggregations must continue to execute server-side (in the database), not client-side after materialization.

- No `.ToList()` / `.AsEnumerable()` / `.AsAsyncEnumerable()` introduced before the `GroupBy` or aggregation in the adapter implementations.
- The generated SQL contains the same `GROUP BY` and aggregate functions as today's queries — verified by inspecting EF query logs for one test run per adapter.
- No additional round-trips: each adapter call produces exactly one SQL query (same as today's two-query baseline — one per method, not one per branch).
- Gap-filling runs in memory in C# (as today); not pushed into SQL.

### NFR-2: Module boundary compliance
After this change, `AnalyticsRepository` depends on zero entities owned by other modules.

- `grep -E "_dbContext\.(IssuedInvoices|BankStatements)" backend/src/Anela.Heblo.Persistence/Features/Analytics/AnalyticsRepository.cs` returns zero matches.
- `using Anela.Heblo.Domain.Features.Invoices;` and `using Anela.Heblo.Domain.Features.Bank;` directives are removed from `AnalyticsRepository.cs` (unless still required for a non-entity type — flag if so).
- New adapter files compile only with references to their own module's namespaces plus `Anela.Heblo.Domain.Features.Analytics` (for the contracts and DTOs they fulfill).

### NFR-3: Backwards compatibility
No external API contract changes. All HTTP endpoints, MediatR handlers, and DTOs exposed to the frontend remain identical in shape and behavior. The OpenAPI-generated TypeScript client must not change.

- Running OpenAPI generation produces a no-op diff for the analytics endpoints.
- `IAnalyticsRepository` public surface remains compatible with all existing callers. Widening `List<>` returns to `IReadOnlyList<>` is allowed only if it does not break any caller; otherwise keep `List<>`.

### NFR-4: Validation gates
The standard project completion gates apply:

- `dotnet build` succeeds with zero new warnings.
- `dotnet format` produces a clean diff.
- All tests touched by the change pass.
- No new analyzer suppressions, `#pragma warning disable`, or `[SuppressMessage]` attributes introduced.

## Data Model

No database schema changes. No migrations.

Existing types (unchanged in shape; only consumption location changes):

- `IssuedInvoice` (Invoices module entity) — now consumed only inside `InvoiceImportStatisticsSourceAdapter`.
- `BankStatementImport` (Bank module entity) — now consumed only inside `BankStatementStatisticsSourceAdapter`.
- `DailyInvoiceCount` (Analytics DTO at `Domain/Features/Analytics/DailyInvoiceCount.cs`) — returned by `IInvoiceImportStatisticsSource`.
- `DailyBankStatementStatistics` (Analytics DTO at `Domain/Features/Analytics/DailyBankStatementStatistics.cs`) — returned by `IBankStatementStatisticsSource`.
- `ImportDateType` enum (already at `Domain/Features/Analytics/`).
- `BankStatementDateType` enum (already at `Domain/Features/Analytics/`).

New types:

- `IInvoiceImportStatisticsSource` interface — `Domain/Features/Analytics/`.
- `IBankStatementStatisticsSource` interface — `Domain/Features/Analytics/`.
- `InvoiceImportStatisticsSourceAdapter` class — `Application/Features/Invoices/Infrastructure/`.
- `BankStatementStatisticsSourceAdapter` class — `Application/Features/Bank/Infrastructure/`.

## API / Interface Design

### Consumer-Owned Contracts (in `Domain/Features/Analytics/`)

```csharp
namespace Anela.Heblo.Domain.Features.Analytics;

/// <summary>
/// Consumer-Owned Contract owned by the Analytics module.
/// Implemented by the Invoices module via an adapter so Analytics can
/// obtain aggregated invoice import counts without depending on the
/// IssuedInvoice entity or the Invoices module's persistence layer.
/// Mirrors the pattern established by <see cref="IAnalyticsProductSource"/>.
/// </summary>
public interface IInvoiceImportStatisticsSource
{
    Task<IReadOnlyList<DailyInvoiceCount>> GetDailyCountsAsync(
        DateTime startDate,
        DateTime endDate,
        ImportDateType dateType,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Consumer-Owned Contract owned by the Analytics module.
/// Implemented by the Bank module via an adapter so Analytics can
/// obtain aggregated bank statement statistics without depending on
/// the BankStatementImport entity or the Bank module's persistence layer.
/// </summary>
public interface IBankStatementStatisticsSource
{
    Task<IReadOnlyList<DailyBankStatementStatistics>> GetDailyStatisticsAsync(
        DateTime startDate,
        DateTime endDate,
        BankStatementDateType dateType,
        CancellationToken cancellationToken = default);
}
```

### Adapter shape (Invoices module)

```csharp
namespace Anela.Heblo.Application.Features.Invoices.Infrastructure;

internal sealed class InvoiceImportStatisticsSourceAdapter : IInvoiceImportStatisticsSource
{
    private readonly IIssuedInvoiceRepository _repository; // or ApplicationDbContext if repository can't satisfy the query

    public InvoiceImportStatisticsSourceAdapter(IIssuedInvoiceRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<DailyInvoiceCount>> GetDailyCountsAsync(
        DateTime startDate,
        DateTime endDate,
        ImportDateType dateType,
        CancellationToken cancellationToken = default)
    {
        // 1) Normalize DateTimeKind to Unspecified for PostgreSQL.
        // 2) Group by year/month/day on InvoiceDate or LastSyncTime per dateType.
        // 3) Project to DailyInvoiceCount rows (Date tagged Utc).
        // 4) Gap-fill missing dates with zero-count rows.
    }
}
```

(Bank adapter follows the identical structure with `IBankStatementImportRepository`, `BankStatementDateType`, and `DailyBankStatementStatistics`.)

### DI registration

Following the precedent of `CatalogAnalyticsSourceAdapter` (registered transient in `CatalogModule`):

- `InvoicesModule.AddInvoicesModule` adds:
  ```csharp
  services.AddTransient<IInvoiceImportStatisticsSource, InvoiceImportStatisticsSourceAdapter>();
  ```
- `BankModule.AddBankModule` adds:
  ```csharp
  services.AddTransient<IBankStatementStatisticsSource, BankStatementStatisticsSourceAdapter>();
  ```
- No registration changes in any Analytics module file.
- No registration changes in `Persistence` infrastructure.

### Refactored `AnalyticsRepository` shape

```csharp
public sealed class AnalyticsRepository : IAnalyticsRepository
{
    private readonly IAnalyticsProductSource _productSource;
    private readonly IInvoiceImportStatisticsSource _invoiceImportStatisticsSource;
    private readonly IBankStatementStatisticsSource _bankStatementStatisticsSource;
    // ApplicationDbContext kept only if still needed for Analytics-owned operations.

    public AnalyticsRepository(
        IAnalyticsProductSource productSource,
        IInvoiceImportStatisticsSource invoiceImportStatisticsSource,
        IBankStatementStatisticsSource bankStatementStatisticsSource)
    {
        _productSource = productSource;
        _invoiceImportStatisticsSource = invoiceImportStatisticsSource;
        _bankStatementStatisticsSource = bankStatementStatisticsSource;
    }

    // StreamProductsWithSalesAsync / GetProductAnalysisDataAsync: unchanged delegation to _productSource.

    public async Task<List<DailyInvoiceCount>> GetInvoiceImportStatisticsAsync(
        DateTime startDate, DateTime endDate, ImportDateType dateType,
        CancellationToken cancellationToken = default) =>
        (await _invoiceImportStatisticsSource.GetDailyCountsAsync(
            startDate, endDate, dateType, cancellationToken)).ToList();

    public async Task<List<DailyBankStatementStatistics>> GetBankStatementImportStatisticsAsync(
        DateTime startDate, DateTime endDate, BankStatementDateType dateType,
        CancellationToken cancellationToken = default) =>
        (await _bankStatementStatisticsSource.GetDailyStatisticsAsync(
            startDate, endDate, dateType, cancellationToken)).ToList();
}
```

## Dependencies

- Pattern reference: `IAnalyticsProductSource` (`Domain/Features/Analytics/`) and `CatalogAnalyticsSourceAdapter` (`Application/Features/Catalog/Infrastructure/`), registered in `CatalogModule`.
- Recent precedent: commit `c296d768` — `feat: Decouple Manufacture from ICatalogRepository via Consumer-Owned Contract`.
- Existing repository abstractions reused by the adapters:
  - `IIssuedInvoiceRepository` (`Application/Features/Invoices/Contracts/`).
  - `IBankStatementImportRepository` (`Domain/Features/Bank/`).
- `docs/architecture/development_guidelines.md` — module boundary rules being enforced.
- No new NuGet packages.
- No external services.

## Out of Scope

- **Per-module `DbContext` split.** This refactor unblocks that future work but does not perform it. Adapters may still use `ApplicationDbContext` if they fall back from the existing repository abstractions; per-module contexts are a separate, larger change.
- **Other cross-module accesses inside `AnalyticsRepository`.** If the repository accesses additional entities owned by other modules beyond `IssuedInvoices` and `BankStatements`, flag them in the PR description for a follow-up. They are not fixed here.
- **Cross-module accesses elsewhere in the codebase.** Only `AnalyticsRepository` is in scope.
- **DTO redesign.** `DailyInvoiceCount` and `DailyBankStatementStatistics` keep their current public shape. Renames, field additions, or type tightening are out of scope.
- **Enum relocation.** `ImportDateType` and `BankStatementDateType` already live in Analytics — no move needed.
- **Performance tuning.** No new indexes, no query rewrites beyond what is necessary to preserve behavior.
- **Frontend changes.** None — the HTTP contract is unchanged.
- **Removing `ApplicationDbContext` from `AnalyticsRepository`.** Allowed if every remaining usage in `AnalyticsRepository` is removed by this change; otherwise the field stays for unrelated Analytics-owned queries.

## Open Questions

None.

## Status: COMPLETE