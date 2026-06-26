## Module
Analytics

## Finding

`backend/src/Anela.Heblo.Persistence/Features/Analytics/AnalyticsRepository.cs`

`AnalyticsRepository` directly queries entities from two other modules via the shared `ApplicationDbContext`:

- **Lines 68–97**: `_dbContext.IssuedInvoices` — `IssuedInvoice` is a `Domain/Features/Invoices/` entity (Invoices module)
- **Lines 164–199**: `_dbContext.BankStatements` — `BankStatementImport` is a `Domain/Features/Bank/` entity (Bank module)

Both branches filter and aggregate directly on those EF entities without going through any interface defined by the owning module.

## Why it matters

`development_guidelines.md` explicitly forbids "Direct access to another module's entities" and "Cross-module database joins." Analytics reaching into Invoices and Bank tables creates tight structural coupling: a schema change in either module's entity propagates directly into Analytics persistence code. It also prevents the Phase 2 goal (per-module DbContexts), because the import-statistics queries would break the moment `IssuedInvoice` and `BankStatementImport` move to separate contexts.

The pattern for exactly this situation is already established in the codebase: `IAnalyticsProductSource` lives in `Domain/Features/Analytics/` and is implemented by `CatalogAnalyticsSourceAdapter` in `Catalog/Infrastructure/` (registered by `CatalogModule`). Invoice and bank statistics should follow the same inversion.

## Suggested fix

1. Add two interfaces to `Domain/Features/Analytics/`:
   ```csharp
   public interface IInvoiceImportStatisticsSource
   {
       Task<List<DailyInvoiceCount>> GetDailyCountsAsync(
           DateTime startDate, DateTime endDate,
           ImportDateType dateType, CancellationToken ct = default);
   }

   public interface IBankStatementStatisticsSource
   {
       Task<List<DailyBankStatementStatistics>> GetDailyStatisticsAsync(
           DateTime startDate, DateTime endDate,
           BankStatementDateType dateType, CancellationToken ct = default);
   }
   ```

2. Implement adapters in the Invoices and Bank modules respectively (each adapter queries its own DbContext / repository), and register them via those modules' DI registration.

3. Replace the direct `_dbContext.IssuedInvoices` / `_dbContext.BankStatements` queries in `AnalyticsRepository` with calls to the injected interfaces.

---
_Filed by daily arch-review routine on 2026-06-03._