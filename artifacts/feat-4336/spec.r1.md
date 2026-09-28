# Specification: Replace per-transaction existence checks with a bulk lookup in MarketingInvoiceImportService

## Summary
`MarketingInvoiceImportService.ImportAsync()` currently issues one `ExistsAsync` database round-trip per transaction to detect already-imported marketing transactions, producing N SQL queries for a batch of N. This spec replaces that per-row check with a single bulk lookup executed once before the loop, eliminating the N+1 query pattern and letting the existing in-memory `stagedIds` deduplication set be removed as redundant.

## Background
`MarketingInvoiceImportService.ImportAsync()` (in `Anela.Heblo.Application.Features.MarketingInvoices.Services`) imports marketing transactions fetched from an ad-platform adapter (Meta Ads, Google Ads) via `IMarketingTransactionSource.GetTransactionsAsync`. For each transaction it checks two things before persisting: (1) whether the transaction ID was already staged earlier in the same run (`stagedIds`, an in-memory `HashSet<string>`), and (2) whether it was already imported in a prior run, via `_repository.ExistsAsync(platform, transactionId, ct)` — a per-row `SELECT EXISTS(...)` query.

For a batch of N transactions this produces N separate round trips to the database purely to answer "which of these IDs do I already have?" — a question a single `WHERE Platform = @platform AND TransactionId IN (...)` query answers in one round trip. This was flagged by the repository's daily architecture-review routine as an N+1 query anti-pattern (see `artifacts/feat-4336/brief.md`, filed 2026-09-28). The fix is scoped to the `MarketingInvoices` module only and touches one repository interface/implementation and one service method.

## Functional Requirements

### FR-1: Add a bulk existence-lookup method to `IImportedMarketingTransactionRepository`
Add a new method to `IImportedMarketingTransactionRepository` (`backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/IImportedMarketingTransactionRepository.cs`) that, given a platform and a collection of transaction IDs, returns the subset of those IDs that already exist in the `ImportedMarketingTransaction` table for that platform, in a single query:

```csharp
Task<HashSet<string>> GetExistingTransactionIdsAsync(
    string platform, IEnumerable<string> transactionIds, CancellationToken ct);
```

Implement it in `ImportedMarketingTransactionRepository` (`backend/src/Anela.Heblo.Persistence/Features/MarketingInvoices/ImportedMarketingTransactionRepository.cs`) using a single `WHERE Platform == platform && transactionIds.Contains(x.TransactionId)` query (materializing `TransactionId` values into a `HashSet<string>`), following the same query style as the existing `ExistsAsync` method (built on the inherited `BaseRepository` query helpers).

The existing `ExistsAsync(string platform, string transactionId, CancellationToken ct)` method stays on the interface unchanged — it is not part of this fix and may still be used elsewhere or by tests.

**Acceptance criteria:**
- `GetExistingTransactionIdsAsync` is declared on `IImportedMarketingTransactionRepository` with the signature above.
- The implementation issues exactly one SQL query per call, regardless of how many transaction IDs are passed in.
- Given a platform and a list of IDs, it returns exactly the subset of those IDs that exist in the store for that platform (case-sensitive, exact match on `TransactionId`, scoped to the given `Platform`) — IDs not present in the store are simply absent from the returned set, no error is raised.
- Passing an empty `transactionIds` collection returns an empty `HashSet<string>` without hitting the database (or, if hitting it, without throwing).
- `ExistsAsync` remains unchanged on both the interface and the implementation.

### FR-2: Use the bulk lookup in `MarketingInvoiceImportService.ImportAsync`
In `ImportAsync` (`backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs`):
1. After fetching `transactions` from the source and before the `foreach` loop, collect all transaction IDs from the fetched batch and call `_repository.GetExistingTransactionIdsAsync(source.Platform, allIds, ct)` exactly once to obtain the set of already-imported IDs (`alreadyImported`).
2. Inside the loop, replace the per-row `await _repository.ExistsAsync(source.Platform, transaction.TransactionId, ct)` call and its `if (exists)` branch with a synchronous check against `alreadyImported.Contains(transaction.TransactionId)`, preserving the existing "already imported — skipping" log message and `result.Skipped++` behavior.
3. Remove the `stagedIds` `HashSet<string>` and its associated `if (stagedIds.Contains(...))` branch (the "already staged in this run" check) and the `stagedIds.Add(transaction.TransactionId)` call, since `alreadyImported` — fetched once from a snapshot of `transactions` taken before the loop starts — cannot detect duplicate IDs *within* the same fetched batch (it was built from a single upfront query, not updated as rows are staged). **Note:** removing `stagedIds` without a replacement changes behavior — see Open Questions.
4. Leave all other logic in `ImportAsync` unchanged: the empty-currency check, the try/catch around each transaction, `AddAsync`, `stagedCount`, `SaveChangesAsync`, and the summary log line all stay exactly as they are today.

**Acceptance criteria:**
- `_repository.GetExistingTransactionIdsAsync` (or `ExistsAsync`, per the resolution of the Open Question below) is called at most once per `ImportAsync` invocation — never inside the `foreach` loop.
- For a batch containing transactions already present in the store, those transactions are skipped and `result.Skipped` is incremented, with the same log message content as before ("already imported — skipping").
- For a batch containing transactions not present in the store, those transactions are added via `_repository.AddAsync` and counted toward `result.Imported` after a successful `SaveChangesAsync`, exactly as before.
- Behavior for empty-`Currency` transactions, transaction-level exceptions (caught, logged, counted as `Failed`), and the final `SaveChangesAsync`/exception-rethrow path is unchanged.
- Existing tests in `MarketingInvoiceImportServiceTests.cs` continue to pass (adapted to mock `GetExistingTransactionIdsAsync` instead of, or in addition to, `ExistsAsync`, per the resolution of the Open Question below).
- New/updated unit tests demonstrate that, for a batch of N transactions, the repository's bulk-lookup method is invoked exactly once (not N times), using the existing repository mock in `MarketingInvoiceImportServiceTests.cs`.

## Non-Functional Requirements

### NFR-1: Performance
The number of database round trips issued by `ImportAsync` for existence checking must be O(1) per import run (one bulk lookup), not O(N) in the number of fetched transactions. No specific latency target is set beyond this — the fix is about round-trip count, not raw query latency. The change must not introduce unbounded query parameter lists in a way that breaks for realistic batch sizes (tens to a few hundred transactions per run, per the brief's stated 50–200 range); no explicit chunking/batching of the `IN (...)` clause is required for this scope.

### NFR-2: Correctness / data integrity
The bulk lookup must not change which transactions are ultimately imported versus skipped, other than the within-run-duplicate-detection change called out in FR-2 and the Open Questions. No transaction that would previously have been correctly skipped as "already imported" may now be incorrectly re-imported, and vice versa.

### NFR-3: Testability
The new repository method must be mockable through the existing test doubles/mocking approach already used for `IImportedMarketingTransactionRepository` in `MarketingInvoiceImportServiceTests.cs` and `ImportMarketingInvoicesHandlerTests.cs`, with no new test infrastructure required.

## Data Model
No schema changes. This fix touches query logic only, against the existing `ImportedMarketingTransaction` entity and its `Platform` / `TransactionId` columns (already indexed/queried by the existing `ExistsAsync` method). No new entities, columns, or migrations are introduced.

## API / Interface Design
This is an internal repository/service-layer change with no externally visible API surface:
- **New repository method:** `Task<HashSet<string>> GetExistingTransactionIdsAsync(string platform, IEnumerable<string> transactionIds, CancellationToken ct)` on `IImportedMarketingTransactionRepository`, implemented in `ImportedMarketingTransactionRepository`.
- **No change** to `IMarketingInvoiceImportService.ImportAsync`'s public signature, to `MarketingImportResult`, or to any controller/MediatR handler that calls into this service (e.g. `ImportMarketingInvoicesHandler` per `ImportMarketingInvoicesHandlerTests.cs`) — those keep calling `ImportAsync` exactly as before.
- No new HTTP endpoints, DTOs, or UI changes.

## Dependencies
- Existing `ApplicationDbContext` / EF Core query infrastructure already used by `BaseRepository` and `ImportedMarketingTransactionRepository.ExistsAsync`.
- Existing test suite (`MarketingInvoiceImportServiceTests.cs`, `ImportMarketingInvoicesHandlerTests.cs`) and its mocking approach for `IImportedMarketingTransactionRepository`.
- No new external services, libraries, or feature flags.

## Out of Scope
- Chunking/paginating the `IN (...)` clause for very large batches (not needed at the 50–200 transaction scale described in the brief).
- Any change to `IMarketingTransactionSource`, the Meta Ads/Google Ads adapters, or how transactions are fetched.
- Any change to `IImportedMarketingTransactionRepository.AddAsync` or `SaveChangesAsync`, or to how staged entities are persisted.
- Removing or refactoring `ExistsAsync` itself (it is left in place; this fix adds a new method alongside it).
- Any broader refactor of `MarketingInvoiceImportService` beyond the existence-check call site (error handling, logging structure, result shape, etc. are unchanged).
- Performance/index tuning at the database level (e.g., adding a composite index on `(Platform, TransactionId)`) — only the query pattern changes in this scope; if no such index exists today, that is a separate concern.

## Open Questions
1. **Should `stagedIds` (within-run duplicate detection) actually be removed?** The brief's suggested fix says `stagedIds` "becomes redundant" because `alreadyImported` is fetched once "before the loop starts." This reasoning is only correct if the *source adapter* (Meta Ads/Google Ads) never returns the same `TransactionId` twice within a single `GetTransactionsAsync` call for the same run — `alreadyImported` is a snapshot taken from the database before the loop, so it has no way to detect a duplicate that first appears twice within the same in-memory `transactions` list (since neither copy is in the database yet, both would pass the `alreadyImported.Contains(...)` check and both would be added via `AddAsync`, likely causing a save-time uniqueness violation instead of a clean in-memory skip). **Assumption made for this spec:** since this has not been confirmed against actual adapter behavior, keep `stagedIds` in place as a safety net for within-run duplicates, and use it as a second, in-memory guard alongside the new `alreadyImported` set: check `stagedIds.Contains(...)` first (unchanged), then `alreadyImported.Contains(...) `in place of the old per-row `ExistsAsync` call, and still add to `stagedIds` after a successful stage. This preserves current behavior while removing the N per-row database round trips; it forgoes the additional simplification the brief hoped for (removing `stagedIds` entirely) until adapter-level duplicate behavior is confirmed. If, during implementation or review, it is confirmed that source adapters never return duplicate `TransactionId`s within one call, `stagedIds` can be removed as the brief originally suggested — the architect/dev team should make this call explicitly rather than silently dropping the within-run guard.
2. **Case sensitivity / exact-match semantics of `TransactionId` comparison in the bulk query.** The existing `ExistsAsync` uses `==` for both `Platform` and `TransactionId`, which this spec's bulk method mirrors (`Contains` on the ID collection, `==` on `Platform`). No change in comparison semantics is intended; flagged only so the implementer keeps the same DB collation/case-sensitivity behavior as the existing method rather than introducing a subtly different comparison (e.g. via `ToUpper()` normalization) as part of this fix.

## Status: HAS_QUESTIONS
