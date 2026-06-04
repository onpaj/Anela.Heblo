# Specification: Decouple AnalyticsRepository from Invoices and Bank Modules

## Summary
Refactor `AnalyticsRepository` so it no longer queries `IssuedInvoices` and `BankStatements` tables directly via the shared `ApplicationDbContext`. Introduce two new domain-level source interfaces in the Analytics module (`IInvoiceImportStatisticsSource`, `IBankStatementStatisticsSource`), implemented by adapters in the Invoices and Bank modules, mirroring the existing `IAnalyticsProductSource` / `CatalogAnalyticsSourceAdapter` pattern.

## Background
`AnalyticsRepository` currently violates the Clean Architecture / Vertical Slice module boundaries codified in `docs/architecture/development_guidelines.md`. Two of its queries reach across modules:

- **Invoice import statistics** (`AnalyticsRepository.cs` lines 68–97) query `_dbContext.IssuedInvoices` directly. `IssuedInvoice` is owned by the Invoices module (`Domain/Features/Invoices/`).
- **Bank statement statistics** (`AnalyticsRepository.cs` lines 164–199) query `_dbContext.BankStatements` directly. `BankStatementImport` is owned by the Bank module (`Domain/Features/Bank/`).

This tight coupling has two consequences:

1. **Schema-change blast radius:** Any change to `IssuedInvoice` or `BankStatementImport` (column rename, type change, relationship adjustment) propagates directly into Analytics persistence code, even though Analytics has no semantic ownership of those entities.
2. **Phase 2 blocker:** The roadmap for per-module `DbContext` instances cannot proceed while Analytics shares a context with Invoices and Bank. Once `IssuedInvoice` and `BankStatementImport` move to module-specific contexts, the existing queries will break compilation.

The established pattern for solving this is already in the codebase. `IAnalyticsProductSource` lives in `Domain/Features/Analytics/`, declared as a Consumer-Owned Contract by Analytics. `CatalogAnalyticsSourceAdapter` (`Catalog/Infrastructure/`) implements it and is registered in `CatalogModule`. Analytics depends only on the interface; Catalog is responsible for fulfilling it. The same inversion must be applied to invoice and bank-statement statistics.

A recent precedent confirms the direction: commit `c296d768` (`feat: Decouple Manufacture from ICatalogRepository via Consumer-Owned Contract`) applied the same pattern to remove a cross-module direct dependency.

## Functional Requirements

### FR-1: Introduce `IInvoiceImportStatisticsSource` consumer contract
Add an interface in `Domain/Features/Analytics/` that exposes only the aggregated read shape Analytics needs from the Invoices module — no direct entity exposure.

**Acceptance criteria:**
- Interface file lives at `backend/src/Anela.Heblo.Domain/Features/Analytics/IInvoiceImportStatisticsSource.cs`.
- Interface declares one method:
  ```csharp
  Task<IReadOnlyList<DailyInvoiceCount>> GetDailyCountsAsync(
      DateTime startDate,
      DateTime endDate,
      ImportDateType dateType,
      CancellationToken cancellationToken = default);
  ```
- `DailyInvoiceCount` is the existing DTO already used inside the current branch of `AnalyticsRepository` (lines 68–97). It must remain a class in `Domain/Features/Analytics/` (or move there if it currently lives elsewhere), and its public shape must be preserved.
- `ImportDateType` enum is already used by the existing query and must be referenced from its current location (no duplication).
- No reference to `IssuedInvoice`, `ApplicationDbContext`, or any Invoices entity in the interface signature.
- XML doc comment on the interface states it is a Consumer-Owned Contract belonging to Analytics, with the implementing module responsible for the data source.

### FR-2: Introduce `IBankStatementStatisticsSource` consumer contract
Add an analogous interface for bank statement statistics.

**Acceptance criteria:**
- Interface file lives at `backend/src/Anela.Heblo.Domain/Features/Analytics/IBankStatementStatisticsSource.cs`.
- Interface declares one method:
  ```csharp
  Task<IReadOnlyList<DailyBankStatementStatistics>> GetDailyStatisticsAsync(
      DateTime startDate,
      DateTime endDate,
      BankStatementDateType dateType,
      CancellationToken cancellationToken = default);
  ```
- `DailyBankStatementStatistics` is the existing aggregation DTO already used inside the current branch of `AnalyticsRepository` (lines 164–199). It must remain a class in `Domain/Features/Analytics/` and its public shape must be preserved.
- `BankStatementDateType` enum is already used by the existing query and must be referenced from its current location.
- No reference to `BankStatementImport`, `ApplicationDbContext`, or any Bank entity in the interface signature.
- XML doc comment on the interface states it is a Consumer-Owned Contract belonging to Analytics.

### FR-3: Implement Invoices adapter
Provide an adapter in the Invoices module that fulfills `IInvoiceImportStatisticsSource`.

**Acceptance criteria:**
- Adapter file lives under `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/` (or the equivalent module-internal infrastructure folder — match where the Invoices module currently keeps its EF-touching infrastructure code). Name it `InvoiceImportStatisticsSourceAdapter`.
- Adapter takes `ApplicationDbContext` (or, if the Invoices module already has its own repository abstraction, that abstraction) via constructor injection.
- Adapter reproduces the exact aggregation behavior currently in `AnalyticsRepository.cs` lines 68–97, including:
  - Date filtering by `ImportDateType` (preserve every branch of the existing switch on date type).
  - Grouping and projection that yields the same `DailyInvoiceCount` rows.
  - Inclusive/exclusive boundary semantics of `startDate` / `endDate` identical to today.
- Registered in DI by the Invoices module's registration code (whichever `*Module.cs` / `AddInvoicesModule` extension method exists) — not by the Analytics module and not by `Persistence` infrastructure.
- Lifetime registration matches the existing Catalog adapter precedent (`CatalogAnalyticsSourceAdapter`).

### FR-4: Implement Bank adapter
Provide an analogous adapter in the Bank module.

**Acceptance criteria:**
- Adapter file lives under the Bank module's infrastructure folder, named `BankStatementStatisticsSourceAdapter`.
- Constructor takes `ApplicationDbContext` (or the Bank module's own data access abstraction if one exists).
- Adapter reproduces the exact behavior in `AnalyticsRepository.cs` lines 164–199, including every branch of the date-type switch and the same projection into `DailyBankStatementStatistics`.
- Registered in DI by the Bank module's registration code.
- Lifetime registration matches the Catalog adapter precedent.

### FR-5: Replace direct queries in `AnalyticsRepository`
Remove the cross-module EF access from `AnalyticsRepository` and route the two operations through the new interfaces.

**Acceptance criteria:**
- `AnalyticsRepository` constructor takes `IInvoiceImportStatisticsSource` and `IBankStatementStatisticsSource` in addition to its existing dependencies.
- Lines 68–97 are replaced by a call to `_invoiceImportStatisticsSource.GetDailyCountsAsync(...)`.
- Lines 164–199 are replaced by a call to `_bankStatementStatisticsSource.GetDailyStatisticsAsync(...)`.
- `AnalyticsRepository.cs` contains no references to `_dbContext.IssuedInvoices` or `_dbContext.BankStatements` after the change.
- `AnalyticsRepository.cs` retains direct `ApplicationDbContext` access **only** for entities that belong to the Analytics module itself. Any other cross-module access discovered during the work is flagged in the PR description (not silently fixed — those are out of scope).
- DI registration of `AnalyticsRepository` is unchanged in terms of registration site; only its constructor signature changes.

### FR-6: Behavior-preserving refactor
This is a refactor with no functional change. All callers of `AnalyticsRepository` must observe identical inputs and outputs.

**Acceptance criteria:**
- Existing unit and integration tests for invoice import statistics and bank statement statistics pass without modification of their assertions (test setup may need to register the new adapters or fakes).
- For any test that previously asserted against in-memory `IssuedInvoices` or `BankStatements` seed data via `AnalyticsRepository`, the test continues to pass either by:
  - Registering the real adapters against the same in-memory `ApplicationDbContext` (preferred — matches today's behavior), or
  - Providing a fake `IInvoiceImportStatisticsSource` / `IBankStatementStatisticsSource` implementation in the test.
- No SQL/EF query shape regressions: the generated SQL for the invoice and bank statistics queries should be functionally equivalent (same predicates, same grouping). Manual verification by enabling EF query logging on one representative test is sufficient.

### FR-7: Test coverage for new adapters
Each new adapter ships with focused tests.

**Acceptance criteria:**
- Unit/integration tests for `InvoiceImportStatisticsSourceAdapter` cover:
  - Each branch of the `ImportDateType` switch.
  - Empty result set.
  - Date range boundary inclusivity.
- Unit/integration tests for `BankStatementStatisticsSourceAdapter` cover:
  - Each branch of the `BankStatementDateType` switch.
  - Empty result set.
  - Date range boundary inclusivity.
- Tests live in the same test project as the existing Invoices / Bank module tests (mirror existing conventions).
- Overall backend test suite passes (`dotnet test`).

## Non-Functional Requirements

### NFR-1: Performance
The refactor must not regress query performance. Aggregations must continue to execute server-side (in the database), not client-side after materialization.

- No `.ToList()` / `.AsEnumerable()` introduced before the `GroupBy` / aggregation in the adapter implementations.
- The generated SQL must contain the same `GROUP BY` and aggregate functions as today's queries — verify by inspecting EF query logs for one test run per adapter.
- No additional round-trips: each adapter call must produce exactly one SQL query (same as today).

### NFR-2: Module boundary compliance
After this change, `AnalyticsRepository` must depend on zero entities owned by other modules.

- `grep -E "_dbContext\.(IssuedInvoices|BankStatements)" backend/src/Anela.Heblo.Persistence/Features/Analytics/AnalyticsRepository.cs` returns zero matches.
- `using Anela.Heblo.Domain.Features.Invoices` and `using Anela.Heblo.Domain.Features.Bank` are removed from `AnalyticsRepository.cs` (unless still required for a non-entity type — flag if so).

### NFR-3: Backwards compatibility
No external API contract changes. All HTTP endpoints, MediatR handlers, and DTOs exposed to the frontend remain identical in shape and behavior. The OpenAPI-generated TypeScript client must not change.

- Running OpenAPI generation produces a no-op diff for the analytics endpoints.

### NFR-4: Validation gates
The standard project completion gates apply:

- `dotnet build` succeeds with zero warnings introduced by this change.
- `dotnet format` produces a clean diff.
- All tests touched by the change pass.

## Data Model

No schema changes. No migrations.

The following existing types remain unchanged in shape; only their location of consumption changes:

- `IssuedInvoice` — Invoices module entity. Now consumed only inside the Invoices module's adapter.
- `BankStatementImport` — Bank module entity. Now consumed only inside the Bank module's adapter.
- `DailyInvoiceCount` — Analytics DTO. Returned by `IInvoiceImportStatisticsSource`.
- `DailyBankStatementStatistics` — Analytics DTO. Returned by `IBankStatementStatisticsSource`.
- `ImportDateType` — Enum used in invoice queries. Lives at its current location (no move).
- `BankStatementDateType` — Enum used in bank statement queries. Lives at its current location (no move).

New types:

- `IInvoiceImportStatisticsSource` interface — `Domain/Features/Analytics/`
- `IBankStatementStatisticsSource` interface — `Domain/Features/Analytics/`
- `InvoiceImportStatisticsSourceAdapter` class — Invoices module infrastructure
- `BankStatementStatisticsSourceAdapter` class — Bank module infrastructure

## API / Interface Design

### Consumer-Owned Contracts (in `Domain/Features/Analytics/`)

```csharp
namespace Anela.Heblo.Domain.Features.Analytics;

/// <summary>
/// Consumer-Owned Contract owned by the Analytics module.
/// Implemented by the Invoices module to provide aggregated invoice import
/// counts without exposing IssuedInvoice entity to Analytics.
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
/// Implemented by the Bank module to provide aggregated bank statement
/// statistics without exposing BankStatementImport entity to Analytics.
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

### DI registration pattern

Following the precedent of `CatalogAnalyticsSourceAdapter`:

- Invoices module's `AddInvoicesModule` (or equivalent) registration extension method adds:
  ```csharp
  services.AddScoped<IInvoiceImportStatisticsSource, InvoiceImportStatisticsSourceAdapter>();
  ```
- Bank module's module registration extension adds the analogous line for the bank adapter.
- No registration changes inside the Analytics module.
- No registration changes inside `Persistence` infrastructure.

### Refactored `AnalyticsRepository` shape

```csharp
public AnalyticsRepository(
    ApplicationDbContext dbContext,
    IInvoiceImportStatisticsSource invoiceImportStatisticsSource,
    IBankStatementStatisticsSource bankStatementStatisticsSource,
    /* …existing dependencies… */)
{
    _dbContext = dbContext;
    _invoiceImportStatisticsSource = invoiceImportStatisticsSource;
    _bankStatementStatisticsSource = bankStatementStatisticsSource;
    /* … */
}
```

The two affected methods delegate to the injected sources; all other methods are unchanged.

## Dependencies

- Existing pattern: `IAnalyticsProductSource` / `CatalogAnalyticsSourceAdapter` (reference implementation to mirror).
- Existing precedent: commit `c296d768` (Manufacture decoupling) — same pattern, recently applied.
- `docs/architecture/development_guidelines.md` — module boundary rules being enforced.
- No new NuGet packages.
- No external service dependencies.

## Out of Scope

- **Per-module `DbContext` split.** This refactor unblocks that future work but does not perform it. Both adapters in this change still receive `ApplicationDbContext` (or an existing repository) — whichever each module currently uses. Migrating each module to its own context is a separate, larger change.
- **Other cross-module accesses inside `AnalyticsRepository`.** If the repository accesses additional entities owned by other modules beyond `IssuedInvoices` and `BankStatements`, those are flagged in the PR description for a follow-up task. They are not fixed here.
- **Cross-module accesses elsewhere in the codebase.** Only `AnalyticsRepository` is in scope.
- **DTO redesign.** `DailyInvoiceCount` and `DailyBankStatementStatistics` keep their current public shape. Renames, field additions, or type tightening are out of scope.
- **Enum relocation.** `ImportDateType` and `BankStatementDateType` stay where they currently live. If their current location creates a circular reference between Analytics and Invoices/Bank, flag it in Open Questions rather than moving them.
- **Performance tuning.** No new indexes, no query rewrites beyond what is necessary to preserve behavior.
- **Frontend changes.** None — the HTTP contract is unchanged.

## Open Questions

None.

## Status: COMPLETE