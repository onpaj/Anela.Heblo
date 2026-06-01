## Module
MarketingInvoices

## Finding
Three related pieces of the module exist solely as scaffolding for a sync workflow that has not been implemented:

**1. `IImportedMarketingTransactionRepository.GetUnsyncedAsync` — zero callers**
```csharp
// Domain/Features/MarketingInvoices/IImportedMarketingTransactionRepository.cs:7
Task<List<ImportedMarketingTransaction>> GetUnsyncedAsync(CancellationToken ct);
```
A grep across the entire `src/` tree finds no call site outside the interface and its implementation.

**2. `ImportedMarketingTransaction.IsSynced` — always `false`, never updated**
```csharp
// Domain/Features/MarketingInvoices/ImportedMarketingTransaction.cs:14
public bool IsSynced { get; set; } = false;
```
Set to `false` on insert (`MarketingInvoiceImportService.cs:74`). No code anywhere sets it to `true`. Every row in the database is permanently unsynced.

**3. `ImportedMarketingTransaction.ErrorMessage` — always `null`, never written**
```csharp
// Domain/Features/MarketingInvoices/ImportedMarketingTransaction.cs:15
public string? ErrorMessage { get; set; }
```
The DB column is configured (`ImportedMarketingTransactionConfiguration.cs:53`), but no application code ever assigns a value. The import service logs errors via `ILogger` and updates `result.Failed`, but never stores the error string on the entity.

## Why it matters
YAGNI: these three items inflate the domain interface, the entity, and the database schema with speculative infrastructure for a future sync step. `GetUnsyncedAsync` appearing in the domain interface misleads readers into assuming it is called somewhere. `IsSynced = false` on every row makes the column queryable but useless. `ErrorMessage` occupies a `text` column that is always `NULL`.

## Suggested fix
Two options depending on intent:

**A — Implement the missing sync step:** Add a use case or background service that fetches unsynced transactions, posts them to the accounting system, and sets `IsSynced = true` / `ErrorMessage` on failure. This is the intended direction.

**B — Remove the scaffolding until it is needed:** Delete `GetUnsyncedAsync` from the interface and implementation, drop `IsSynced` and `ErrorMessage` from `ImportedMarketingTransaction` and its EF configuration, and add a migration to remove the columns. Re-add when the sync workflow is actually being built.

Option B is cheaper now; option A is the right call only when the sync workflow is actively being built.

---
_Filed by daily arch-review routine on 2026-05-26._