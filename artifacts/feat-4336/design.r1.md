# Design: Bulk existence lookup for MarketingInvoiceImportService

## Component Design

No new components or module boundaries. Three existing files change; nothing else in the `MarketingInvoices` slice is touched.

### `IImportedMarketingTransactionRepository` (Domain/Features/MarketingInvoices)

Add one method alongside the existing three; nothing is removed.

```csharp
public interface IImportedMarketingTransactionRepository
{
    Task<bool> ExistsAsync(string platform, string transactionId, CancellationToken ct);

    // NEW
    Task<HashSet<string>> GetExistingTransactionIdsAsync(
        string platform, IEnumerable<string> transactionIds, CancellationToken ct);

    Task<ImportedMarketingTransaction> AddAsync(ImportedMarketingTransaction entity, CancellationToken ct);
    Task<int> SaveChangesAsync(CancellationToken ct);
}
```

**Responsibility:** given a platform and a candidate set of transaction IDs, answer "which of these already exist in the store for this platform?" in exactly one query. It does not deduplicate its input, does not touch `Currency`/`Amount`/other columns, and does not raise for IDs that aren't found — absence from the returned set *is* the "not found" signal.

### `ImportedMarketingTransactionRepository` (Persistence/Features/MarketingInvoices)

Implements the new method on `BaseRepository<ImportedMarketingTransaction, int>`, using the `DbSet` directly (the same access pattern `ManufacturedProductInventoryRepository.GetByProductCodesWithLogsAsync` uses for its `Select`/projection query, since `BaseRepository.FindAsync`'s `Expression<Func<TEntity,bool>>` signature has no room for a projection):

```csharp
public async Task<HashSet<string>> GetExistingTransactionIdsAsync(
    string platform, IEnumerable<string> transactionIds, CancellationToken ct)
{
    var ids = transactionIds is ICollection<string> c ? c : transactionIds.ToList();
    if (ids.Count == 0)
        return new HashSet<string>();

    var existing = await DbSet
        .Where(x => x.Platform == platform && ids.Contains(x.TransactionId))
        .Select(x => x.TransactionId)
        .ToListAsync(ct);

    return existing.ToHashSet();
}
```

- Early-return on an empty `transactionIds` collection, before touching `DbSet` — matches the `ManufacturedProductInventoryRepository` precedent, and satisfies the spec's "empty input must not hit the database (or must not throw if it does)" requirement.
- Comparison is plain `==` (`Platform`) / `Contains` (`TransactionId`) — no `.ToUpper()`/`.ToLower()` normalization, matching `ExistsAsync`'s existing semantics byte-for-byte (Postgres `varchar` equality is collation-independent; see architecture review Decision 2).
- `ExistsAsync` is left in place, unmodified, on both interface and implementation.

### `MarketingInvoiceImportService.ImportAsync` (Application/Features/MarketingInvoices/Services)

**Responsibility unchanged**: fetch transactions from the platform adapter, skip what's already known, persist what's new, report counts. Only the existence-check mechanics inside the method change.

Before the loop (new step, once):

```csharp
var transactions = await source.GetTransactionsAsync(from, to, ct);

var allIds = transactions.Select(t => t.TransactionId).ToList();
var alreadyImported = await _repository.GetExistingTransactionIdsAsync(source.Platform, allIds, ct);

var result = new MarketingImportResult();
var stagedCount = 0;
var stagedIds = new HashSet<string>();
```

Inside the loop, the checks run in this order (cheapest first), replacing only the per-row DB call:

1. Empty-`Currency` check — unchanged.
2. `stagedIds.Contains(transaction.TransactionId)` — **kept, unchanged**. This guards against a duplicate `TransactionId` appearing twice within the same fetched batch, which `alreadyImported` (a pre-loop DB snapshot) cannot detect, since neither copy exists in the database yet. Per the architecture review, removing this would let two in-batch duplicates both pass the `alreadyImported` check, both get `AddAsync`'d, and then violate the unique `(Platform, TransactionId)` index at `SaveChangesAsync` time — which rethrows and fails the *entire* run's persistence, not just the duplicate row. Kept unconditionally, not as a defensive fallback.
3. `alreadyImported.Contains(transaction.TransactionId)` — **new**, replaces the old `await _repository.ExistsAsync(source.Platform, transaction.TransactionId, ct)` call and its `if (exists)` branch. Synchronous, in-memory; same "already imported — skipping" log message; same `result.Skipped++`.
4. Build entity, `AddAsync`, `stagedIds.Add(transaction.TransactionId)`, `stagedCount++` — unchanged.

Everything else — the outer try/catch per transaction (`Failed++`), the empty-`Currency` branch, `SaveChangesAsync` and its try/catch/rethrow, and the summary log line — is untouched.

**Net round-trip count:** 1 (the new bulk lookup) + 1 (`SaveChangesAsync`, only if `stagedCount > 0`), versus the previous 1 (`SaveChangesAsync`) + N (`ExistsAsync`, one per transaction).

### Collaborators — no change

- `IMarketingTransactionSource.GetTransactionsAsync` — call site and contract unchanged.
- `MarketingImportResult` — shape unchanged (`Imported`, `Skipped`, `Failed`).
- `ImportMarketingInvoicesHandler` and any other caller of `IMarketingInvoiceImportService.ImportAsync` — public signature unchanged, no caller-visible change.
- `MarketingInvoicesModule.cs` DI registration — unchanged; the existing `AddScoped<IImportedMarketingTransactionRepository, ImportedMarketingTransactionRepository>()` binding already covers the new method on the same interface/implementation pair.

## Data Schemas

No schema or migration changes. This is a query-pattern change against the existing `ImportedMarketingTransaction` table and its existing unique index on `(Platform, TransactionId)` (`ImportedMarketingTransactionConfiguration.cs`).

### Repository method contract

```csharp
Task<HashSet<string>> GetExistingTransactionIdsAsync(
    string platform,
    IEnumerable<string> transactionIds,
    CancellationToken ct);
```

| Input | Behavior |
|---|---|
| `platform` | Exact-match (`==`) against `ImportedMarketingTransaction.Platform`, same as `ExistsAsync`. |
| `transactionIds` empty | Returns `new HashSet<string>()` without querying the database. |
| `transactionIds` non-empty | Returns the subset of the given IDs that exist in the store for `platform` (case-sensitive, exact match, scoped to `platform`). IDs not present are simply absent — no exception, no per-ID error reporting. |

Underlying query (conceptual SQL, one round trip regardless of ID-list size):

```sql
SELECT "TransactionId"
FROM "ImportedMarketingTransactions"
WHERE "Platform" = @platform
  AND "TransactionId" = ANY(@transactionIds)
```

### In-memory shapes inside `ImportAsync`

| Name | Type | Lifetime | Purpose |
|---|---|---|---|
| `allIds` | `List<string>` | Built once, before the loop | Input to the bulk lookup; all `TransactionId`s in the fetched batch. |
| `alreadyImported` | `HashSet<string>` | Fetched once, before the loop; read-only inside the loop | Prior-run duplicates — replaces the per-row `ExistsAsync` call. |
| `stagedIds` | `HashSet<string>` | Empty at loop start; mutated inside the loop (`.Add(...)` after each successful stage) | Within-run duplicate guard; unchanged from current behavior. |

No changes to `ImportedMarketingTransaction`'s columns, to `MarketingImportResult`'s fields, or to any request/response DTO — this fix has no externally visible API surface.
