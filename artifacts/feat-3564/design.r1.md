# Design: Collapse `IssuedInvoiceRepository.GetSyncStatsAsync` into a single aggregate query

## Component Design

No new components are introduced. This is a body-only rewrite of one repository method plus one new test class; module boundaries, the interface, and the DTO are all unchanged.

### `IssuedInvoiceRepository.GetSyncStatsAsync` (`backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs`, lines 35–58)

**Responsibility:** Given a `[fromDate, toDate]` window (inclusive on `InvoiceDate.Date`), return an `IssuedInvoiceSyncStats` aggregate in exactly one database round trip.

**Contract (unchanged):**
```csharp
Task<IssuedInvoiceSyncStats> GetSyncStatsAsync(
    DateTime fromDate,
    DateTime toDate,
    CancellationToken cancellationToken = default)
```

**Internal implementation strategy:**
1. Build the existing filtered `IQueryable<IssuedInvoice>`: `x.InvoiceDate >= fromDate.Date && x.InvoiceDate <= toDate.Date`.
2. Project all five aggregates in a single `GroupBy(_ => 1).Select(...)` shape, executed with one `FirstOrDefaultAsync`:
   - `Total = g.Count()`
   - `Synced = g.Count(x => x.IsSynced)`
   - `WithErrors = g.Count(x => x.ErrorType.HasValue)`
   - `Critical = g.Count(x => x.ErrorType.HasValue && x.ErrorType != IssuedInvoiceErrorType.InvoicePaired)`
   - `LastSyncTime = g.Where(x => x.LastSyncTime.HasValue).Max(x => (DateTime?)x.LastSyncTime)`
3. `GroupBy(_ => 1)` yields zero groups (not one zero-valued group) when the filtered set is empty, so the projection result is `null` in that case. Map `null` to the same empty-result shape the five-query version produced: all counts `0`, `LastSyncTime = null`.
4. `UnsyncedInvoices` remains a derived, in-memory value: `TotalInvoices - SyncedInvoices`. It is not part of the SQL projection.

**Non-goals for this component:** no signature change, no change to `IIssuedInvoiceRepository`, no change to other repository methods (`GetPaginatedAsync`, `GetHeadersByDateAsync`), no caching layer.

**Downstream consumers (unaffected, verify-only):** `GetIssuedInvoiceSyncStatsHandler` → `InvoicesController` → frontend `useIssuedInvoiceSyncStats` hook. None require code changes; the call chain and DTO shape are identical before and after.

### `IssuedInvoiceRepositoryGetSyncStatsSqlShapeTests` (new, `backend/test/Anela.Heblo.Tests/Features/Invoices/IssuedInvoiceRepositoryGetSyncStatsSqlShapeTests.cs`)

**Responsibility:** Make the "one round trip" claim observable and enforced in CI against a real relational provider, since the InMemory provider used by the existing correctness tests cannot detect a regression back to multiple round trips.

**Design, modeled directly on `PhotobankRepositoryGetTagsSqlShapeTests.cs`:**
- `[Collection("PostgresIntegration")]`, `[Trait("Category", "Integration")]`.
- Constructor takes `PostgresSharedContainerFixture` (shared Testcontainers `postgres:16` instance already used by Photobank/Purchase/Article suites) and calls `_fixture.CreateDatabaseAsync("issuedinvoices")` to get an isolated database per test run.
- Minimal hand-written schema containing only the columns the query touches: `Id`, `InvoiceDate`, `IsSynced`, `ErrorType`, `LastSyncTime`. No need to replicate the full EF-generated schema or foreign keys.
- A private `CapturingCommandInterceptor : DbCommandInterceptor` local to this test class (copied, not shared, per the existing non-DRY-on-purpose convention in the Photobank/Purchase/Article examples) that records every `CommandText` sent to the server.
- Two `[Fact]`s:
  1. Seed rows, call `GetSyncStatsAsync`, assert `interceptor.Commands.Should().HaveCount(1)` — the round-trip guarantee.
  2. Seed a representative row mix (synced/unsynced, `ErrorType` including and excluding `InvoicePaired`, some with/without `LastSyncTime`), call `GetSyncStatsAsync`, and assert the returned `IssuedInvoiceSyncStats` values against real Postgres `GroupBy`/`Count`/`Max` translation — belt-and-suspenders correctness on top of the InMemory suite.
- Assertions target round-trip count and general query shape (e.g., that it hits the seeded table once), not exact `FILTER`/`CASE WHEN` SQL text, so the test doesn't couple to a specific EF Core translation choice.

**Relationship to existing test file:** `IssuedInvoiceRepositoryTests.cs` (InMemory-backed) stays as-is for fast correctness coverage, extended per the spec's FR-2 to additionally assert `LastSyncTime` for both a mixed set and an all-null set. It does not gain a Postgres/Testcontainers dependency — that lives solely in the new `SqlShapeTests` class.

## Data Schemas

No schema, entity, or DTO changes.

### `IssuedInvoiceSyncStats` (`backend/src/Anela.Heblo.Domain/Features/Invoices/IssuedInvoiceSyncStats.cs`) — unchanged shape

| Field | Type | Source under new implementation |
|---|---|---|
| `TotalInvoices` | `int` | `stats?.Total ?? 0` |
| `SyncedInvoices` | `int` | `stats?.Synced ?? 0` |
| `UnsyncedInvoices` | `int` | derived: `TotalInvoices - SyncedInvoices` (in-memory, not part of SQL projection) |
| `InvoicesWithErrors` | `int` | `stats?.WithErrors ?? 0` |
| `CriticalErrors` | `int` | `stats?.Critical ?? 0` (excludes `ErrorType == InvoicePaired`) |
| `LastSyncTime` | `DateTime?` | `stats?.LastSyncTime` (max over rows with non-null `LastSyncTime`, or `null`) |
| `SyncSuccessRate` | `decimal` (computed) | unchanged, derived from the above |

### Query shape (target SQL, provider-translated by Npgsql — exact syntax not asserted by tests)

Single statement against `issued_invoices`, filtered by `InvoiceDate` range, equivalent to:

```sql
SELECT
  COUNT(*)                                                                    AS total,
  COUNT(*) FILTER (WHERE is_synced)                                          AS synced,
  COUNT(*) FILTER (WHERE error_type IS NOT NULL)                             AS with_errors,
  COUNT(*) FILTER (WHERE error_type IS NOT NULL AND error_type <> 'InvoicePaired') AS critical,
  MAX(last_sync_time) FILTER (WHERE last_sync_time IS NOT NULL)              AS last_sync_time
FROM issued_invoices
WHERE invoice_date >= @fromDate AND invoice_date <= @toDate;
```

No new API request/response shapes and no event payloads — the change is entirely internal to one repository method's execution strategy.
