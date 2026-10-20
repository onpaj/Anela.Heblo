# Architecture Review: Bulk existence lookup for MarketingInvoiceImportService

## Skip Design: true

Backend-only query-pattern fix inside a MediatR-adjacent application service and its repository. No new/changed API contract, controller, DTO, or UI surface. `MarketingImportResult` and `IMarketingInvoiceImportService.ImportAsync`'s signature are untouched.

## Architectural Fit Assessment

This fits the existing repository pattern cleanly and requires no structural change. `IImportedMarketingTransactionRepository` already sits in `Domain/Features/MarketingInvoices/`, implemented in `Persistence/Features/MarketingInvoices/ImportedMarketingTransactionRepository.cs` via `BaseRepository<ImportedMarketingTransaction, int>` (ADR-002), with its DI binding correctly living in `MarketingInvoicesModule.cs` (ADR-004) rather than `PersistenceModule`. Adding one method to the interface and its implementation is a same-slice, same-file-set change — no module boundary is crossed and no new module dependency is introduced.

The proposed `Contains`-based bulk lookup is not a novel pattern for this codebase: `ManufacturedProductInventoryRepository.GetByProductCodesWithLogsAsync` (`backend/src/Anela.Heblo.Persistence/Manufacture/Inventory/ManufacturedProductInventoryRepository.cs:20-30`) already does exactly this shape — early-return `Array.Empty<T>()` on an empty input collection, then `DbSet.Where(x => ids.Contains(x.Key))`. The new method should follow that precedent verbatim (early-return before touching `DbSet`), not invent a new idiom.

Critically, code-reading resolves both Open Questions the spec left open (see **Specification Amendments**). I read the EF Core configuration, not just the interface, before finalizing this guidance:

- `ImportedMarketingTransactionConfiguration.cs:62-64` declares a **unique index** on `(Platform, TransactionId)` (`IX_ImportedMarketingTransactions_Platform_TransactionId`). This is the load-bearing fact for Open Question 1.
- `TransactionId`/`Platform` are plain `character varying` columns with no `citext` type and no explicit collation override — Postgres `=`/`IN` comparisons on `varchar`/`text` are always byte-exact regardless of collation (collation only affects ordering, not equality). This is the load-bearing fact for Open Question 2.

## Proposed Architecture

### Component Overview

```
MarketingInvoiceImportService.ImportAsync(source, from, to, ct)
        │
        │ 1. transactions = source.GetTransactionsAsync(...)
        │ 2. allIds = transactions.Select(t => t.TransactionId).ToList()
        │ 3. alreadyImported = repository.GetExistingTransactionIdsAsync(platform, allIds, ct)   ← NEW, single round trip
        ▼
   foreach transaction in transactions:
        │  a. empty-Currency check           (unchanged)
        │  b. stagedIds.Contains(...)?        (unchanged — kept, see Amendments)
        │  c. alreadyImported.Contains(...)?  (NEW — replaces per-row ExistsAsync call)
        │  d. AddAsync + stagedIds.Add(...)   (unchanged)
        ▼
   SaveChangesAsync (unchanged, still one call after the loop)

IImportedMarketingTransactionRepository                  (Domain/Features/MarketingInvoices)
   + GetExistingTransactionIdsAsync(platform, ids, ct)    ← NEW method
     ExistsAsync(platform, id, ct)                          unchanged, left in place
     AddAsync / SaveChangesAsync                             unchanged

ImportedMarketingTransactionRepository : BaseRepository<...>  (Persistence/Features/MarketingInvoices)
   + GetExistingTransactionIdsAsync → DbSet.Where(x => x.Platform == platform
                                          && ids.Contains(x.TransactionId))
                                       .Select(x => x.TransactionId)
                                       .ToListAsync → HashSet<string>
```

Net effect: the per-run DB round-trip count for existence checking goes from N (one `SELECT EXISTS` per transaction) to 1 (one `SELECT ... WHERE Platform = @p AND TransactionId = ANY(@ids)`). Everything downstream of the existence check (AddAsync, SaveChangesAsync, try/catch, logging, `MarketingImportResult` shape) is untouched.

### Key Design Decisions

#### Decision 1: Keep `stagedIds` as a mandatory in-memory guard, not an optional safety net

**Options considered:**
- (a) Remove `stagedIds` entirely, as the brief originally suggested, relying solely on `alreadyImported`.
- (b) Keep `stagedIds` as an optional/defensive safety net pending confirmation of adapter behavior (the spec's Open-Question-1 hedge).
- (c) Keep `stagedIds` unconditionally, as a required part of the design.

**Chosen approach:** (c).

**Rationale:** `ImportedMarketingTransactionConfiguration.cs` declares a **unique index** on `(Platform, TransactionId)`. `alreadyImported` is a snapshot fetched once from the database *before* the loop starts, so it cannot see a duplicate `TransactionId` that first appears twice within the same in-memory `transactions` list — neither copy is in the database yet, so both pass `alreadyImported.Contains(...)` and both get staged via `AddAsync`. Under option (a), that produces two entities in the change tracker that violate the same unique index at `SaveChangesAsync` time. That call is wrapped in a single try/catch that logs and **rethrows** (`MarketingInvoiceImportService.cs:96-108`) — so a same-batch duplicate would fail the *entire* batch's save, turning what should be "skip one duplicate row" into "zero transactions imported this run, exception propagates to the caller." That is strictly worse than today's behavior and is not an acceptable trade for removing one `HashSet` and one `if`. This is true independent of whether the Meta/Google adapters are currently known to emit duplicates — the failure mode this guards against is a save-time throw for the *entire* batch, not a per-row correctness nuance, so it does not require adapter-behavior confirmation to decide. `stagedIds` stays, unconditionally, as designed in the spec's assumption — this review promotes that assumption to a decision.

Ordering matters for cost, not correctness: keep the existing `stagedIds.Contains(...)` check *before* the new `alreadyImported.Contains(...)` check, exactly as the spec's FR-2 describes — it's a free in-memory lookup that should short-circuit before consulting the (larger) DB-backed set.

#### Decision 2: Bulk query mirrors `ExistsAsync`'s comparison semantics exactly — no normalization

**Options considered:**
- (a) Normalize case (e.g. `ToUpper()`) on both sides of the bulk comparison "to be safe."
- (b) Use plain `==`/`Contains` with no normalization, identical to `ExistsAsync`.

**Chosen approach:** (b).

**Rationale:** `ExistsAsync` compares with plain `==` (`ImportedMarketingTransactionRepository.cs:16-18`). `TransactionId`/`Platform` are `character varying` columns with no `citext` type and no collation override in `ImportedMarketingTransactionConfiguration.cs`. Postgres string equality (`=`, and therefore EF's translated `IN`/`ANY`) is byte-exact regardless of column collation — collation affects `ORDER BY`/`<`/`>`, never `=`. Introducing `ToUpper()`/`ToLower()` on either side would be a *new*, narrower matching behavior than what `ExistsAsync` does today, is unsupported by any evidence in the schema, and materially changes correctness (two IDs differing only in case would suddenly be treated as the same transaction). Implement the bulk method with a plain `x.Platform == platform && transactionIds.Contains(x.TransactionId)` predicate — no normalization, byte-for-byte identical semantics to `ExistsAsync`. This is also the established codebase idiom (see `ManufacturedProductInventoryRepository.GetByProductCodesWithLogsAsync`, which uses plain `Contains` for exact-match code lookups).

## Implementation Guidance

### Directory / Module Structure

No new files, no new directories, no DI changes:

- `backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/IImportedMarketingTransactionRepository.cs` — add one method signature.
- `backend/src/Anela.Heblo.Persistence/Features/MarketingInvoices/ImportedMarketingTransactionRepository.cs` — implement it.
- `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs` — call it once before the loop; replace the per-row `ExistsAsync` branch with the `alreadyImported.Contains(...)` check; keep `stagedIds` exactly as it is today (per Decision 1, do **not** remove it, contrary to the brief's original suggestion).
- `backend/src/Anela.Heblo.Application/Features/MarketingInvoicesModule.cs` — **no change**; the existing `AddScoped<IImportedMarketingTransactionRepository, ImportedMarketingTransactionRepository>()` binding already covers the new method (same interface, same implementation type).

### Interfaces and Contracts

```csharp
// IImportedMarketingTransactionRepository — add, do not remove ExistsAsync
Task<HashSet<string>> GetExistingTransactionIdsAsync(
    string platform, IEnumerable<string> transactionIds, CancellationToken ct);
```

Implementation, following the `ManufacturedProductInventoryRepository` precedent (early-return on empty input, plain `Contains`, no normalization):

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

(`DbSet` is the protected member exposed by `BaseRepository<TEntity, TKey>`, same access pattern `ExistsAsync`'s sibling methods use via `AnyAsync`; using `DbSet` directly here — rather than the generic `FindAsync` helper — is required because this needs a `Select` projection to `TransactionId`, which `FindAsync`'s `Expression<Func<TEntity,bool>>` signature does not support. This mirrors `ManufacturedProductInventoryRepository.GetByProductCodesWithLogsAsync`.)

This is an internal Domain/Persistence contract — not a `contracts/` DTO, so the DTOs-are-classes rule does not apply here (no OpenAPI/client-generation surface is touched).

### Data Flow

1. `ImportAsync` fetches `transactions` from the source adapter (unchanged).
2. **New:** immediately after, `allIds = transactions.Select(t => t.TransactionId).ToList()`; call `_repository.GetExistingTransactionIdsAsync(source.Platform, allIds, ct)` once, storing the result as `alreadyImported`.
3. Loop body, in order, per transaction:
   a. empty-`Currency` check → `Failed++`, `continue` (unchanged).
   b. `stagedIds.Contains(id)` → `Skipped++`, `continue` (unchanged — **kept**, see Decision 1).
   c. `alreadyImported.Contains(id)` → `Skipped++`, `continue` (**replaces** the old `await _repository.ExistsAsync(...)` call — same log message, same `Skipped++`).
   d. Build entity, `AddAsync`, `stagedIds.Add(id)`, `stagedCount++` (unchanged).
   e. try/catch around the whole iteration → `Failed++` on exception (unchanged).
4. After the loop: `SaveChangesAsync` if `stagedCount > 0`, same try/catch/rethrow (unchanged).
5. Summary log line (unchanged).

Net: one DB round trip added before the loop, zero DB round trips inside the loop for existence checking (down from N).

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Very large `IN (...)` parameter list if a batch grows far beyond the brief's stated 50–200 range | Low | Explicitly out of scope per the spec (NFR-1); no chunking needed at this scale. If lookback windows are extended enough to change this, revisit with a chunked `IN` or a temp-table join — not now. |
| `SaveChangesAsync` still throws on a genuine cross-run race (two overlapping import runs for the same platform inserting the same new `TransactionId` concurrently) | Low (pre-existing) | Unchanged by this fix — the unique index already protects data integrity in that case exactly as it does today; this fix does not add or remove that exposure. |
| A future change accidentally normalizes case in the new method (e.g. a well-meaning `ToUpper()` "cleanup") | Low | Decision 2 is explicit and should be called out in code review: no case normalization, matches `ExistsAsync` byte-for-byte. |
| Someone removes `stagedIds` in a later "simplification" pass, re-introducing the whole-batch-save-failure risk | Medium | Decision 1's rationale (unique index + rethrow-on-save) should be captured as a code comment on `stagedIds` in `MarketingInvoiceImportService.cs`, not just in this document, so the reasoning survives outside `artifacts/`. |

## Specification Amendments

Both Open Questions in `spec.r1.md` are resolved decisively here, based on reading `ImportedMarketingTransactionConfiguration.cs` (not left to the developer to re-litigate):

1. **`stagedIds` must be kept, unconditionally** — not as a conditional "safety net pending confirmation," as the spec hedged. `ImportedMarketingTransactionConfiguration.cs:62-64` proves a unique `(Platform, TransactionId)` index exists, and `MarketingInvoiceImportService.cs`'s `SaveChangesAsync` catch block rethrows on failure. Removing `stagedIds` risks a same-batch duplicate causing the *entire* run's `SaveChangesAsync` to throw, which is a strictly worse outcome than today. This holds regardless of whether Meta/Google adapters are confirmed to emit duplicates — it is not gated on adapter-behavior confirmation. FR-2's step 3 ("Remove the `stagedIds` ... **Note:** removing `stagedIds` without a replacement changes behavior — see Open Questions") is **superseded**: `stagedIds` is retained exactly as it exists today, ordered before the new `alreadyImported` check.
2. **No case normalization in the bulk query** — confirmed by reading the entity configuration (no `citext`, no collation override) that Postgres `=`/`IN` on these `character varying` columns is byte-exact, identical to `ExistsAsync`'s `==`. Implement `GetExistingTransactionIdsAsync` with a plain `Contains`/`==` predicate, no `ToUpper()`/`ToLower()`. This is not an assumption to revisit later; it is settled by the schema as it exists today.

No other amendments to `spec.r1.md`'s functional or non-functional requirements are needed — FR-1, FR-2 (steps 1, 2, 4), NFR-1, NFR-2, NFR-3, and the Data Model / API sections are architecturally sound as written and should be implemented as specified, with FR-2 step 3 read as amended above.

## Prerequisites

None. No migration, no config, no feature flag, no infrastructure change is required — this is a pure query-logic change against an existing table, existing index, and existing DI registration. Implementation can start immediately.
