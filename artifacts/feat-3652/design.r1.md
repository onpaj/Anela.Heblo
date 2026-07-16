# Design: Remove Analytics dependency from Bank's `IBankStatementImportRepository`

## Component Design

### `BankDailyCount` (new, Domain layer)
- **Location:** `backend/src/Anela.Heblo.Domain/Features/Bank/BankDailyCount.cs`, namespace `Anela.Heblo.Domain.Features.Bank`.
- **Shape:** `public record BankDailyCount(DateTime Date, int ImportCount, int TotalItemCount);`
- **Responsibility:** Bank-owned, Analytics-agnostic carrier of a single day's raw statement counts. Structurally identical to Analytics' `DailyBankStatementStatistics` but owned and versioned independently by Bank, following the existing `DailyInvoiceCount` precedent in the Invoices module. It is a flat, standalone file directly under `Domain/Features/Bank/` — no new subfolder.
- **Consumers:** `IBankStatementImportRepository.GetDailyCountsAsync` (return type), `BankStatementStatisticsSourceAdapter` (input to its mapping step). No other Domain/Features/Bank file needs to reference Analytics as a result of introducing this type.

### `IBankStatementImportRepository` (modified, Domain layer)
- **Location:** `backend/src/Anela.Heblo.Domain/Features/Bank/IBankStatementImportRepository.cs`.
- **Contract change:** `GetDailyStatisticsAsync(DateTime, DateTime, BankStatementDateType, CancellationToken) : Task<IReadOnlyList<DailyBankStatementStatistics>>` is removed and replaced by:
  ```csharp
  Task<IReadOnlyList<BankDailyCount>> GetDailyCountsAsync(
      DateTime startDate,
      DateTime endDate,
      bool byStatementDate,
      CancellationToken cancellationToken = default);
  ```
- **Responsibility:** Declares Bank's own date-ranged, per-day count query, scoped entirely to Bank's domain vocabulary. `byStatementDate` replaces the Analytics enum `BankStatementDateType` as a plain two-state selector (`true` = group/filter by statement date, `false` = group/filter by import date) — no Bank-owned enum is introduced, since only two states exist and a third is not foreseeable.
- **Boundary effect:** The `using Anela.Heblo.Domain.Features.Analytics;` import is dropped from this file entirely. No other method on this interface (`GetFilteredAsync`, `GetByIdAsync`, `AddAsync`, `GetExistingResultsByTransferIdsAsync`, `GetMaxStatementDateAsync`, `GetByTransferIdAsync`, `UpdateAsync`) is affected.

### `BankStatementImportRepository` (modified, Persistence/EF Core layer)
- **Location:** `backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportRepository.cs` (current `GetDailyStatisticsAsync` implementation at lines 143–195).
- **Responsibility:** Implements `GetDailyCountsAsync` against `ApplicationDbContext.BankStatements`:
  - Branches on `byStatementDate` (`if`/`else`) instead of switching on `BankStatementDateType`, selecting which date column (statement date vs. import date) drives the grouping — this is a mechanical control-flow substitution; the two existing LINQ query bodies are otherwise unchanged.
  - Groups by Year/Month/Day of the selected date column; projects `ImportCount` (row count) and `TotalItemCount` (`Sum(ItemCount)`) per calendar day — identical aggregation semantics to today.
  - Constructs and returns `BankDailyCount` instances instead of `DailyBankStatementStatistics`. This method must not construct, return, or import `DailyBankStatementStatistics` or `BankStatementDateType`.
  - Drops the `using Anela.Heblo.Domain.Features.Analytics;` import once no longer referenced.
- **Boundary effect:** This is the last place inside Bank's Persistence/Domain path where the Analytics projection lived; after this change it only produces Bank-owned data.

### `BankStatementStatisticsSourceAdapter` (modified, Application layer — cross-module seam)
- **Location:** `backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/BankStatementStatisticsSourceAdapter.cs`.
- **Responsibility:** Remains the sole translation point between Bank's internal types and Analytics' contract types, implementing the unchanged Analytics-owned interface `IBankStatementStatisticsSource.GetDailyStatisticsAsync`. Internally:
  1. Normalizes `startDate`/`endDate` to UTC (unchanged).
  2. Calls `_repository.GetDailyCountsAsync(startDate, endDate, dateType == BankStatementDateType.StatementDate, cancellationToken)` — translating the Analytics enum into Bank's boolean selector at the seam.
  3. Maps each returned `BankDailyCount` to a `DailyBankStatementStatistics { Date, ImportCount, TotalItemCount }`, via a `resultsByDate` dictionary keyed by date.
  4. Preserves the existing gap-fill loop unchanged: iterates `startDate.Date` to `endDate.Date` inclusive, emitting a zero-count `DailyBankStatementStatistics` for any date absent from the repository result.
- **Boundary effect:** This is the only file in the call graph permitted to import both `Anela.Heblo.Domain.Features.Bank` and `Anela.Heblo.Domain.Features.Analytics` — by design, since a cross-module adapter's job is to sit at the seam. Its public method name, signature, and `IBankStatementStatisticsSource` conformance do not change; only the internal call site and mapping logic change. Output must remain field-for-field, byte-for-byte equivalent to the pre-refactor adapter output for any given `(startDate, endDate, dateType)`.

### `ModuleBoundariesTests.cs` — new `"Bank (Domain) -> Analytics"` rule (recommended, FR-5)
- **Location:** `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`.
- **Responsibility:** Adds a `ModuleBoundaryRule` forbidding `Anela.Heblo.Domain.Features.Bank` from referencing `Anela.Heblo.Domain.Features.Analytics`, `Anela.Heblo.Application.Features.Analytics`, or `Anela.Heblo.Persistence.Analytics`, mirroring the existing forward-direction `"Analytics (Domain) -> Bank"` rule but in reverse. This closes the exact gap that let the original violation land undetected (`dotnet build` and the existing suite had no rule covering this direction).
- **Included in this design** because it is the automated enforcement mechanism for the architectural boundary this entire refactor exists to restore (NFR-3) — without it, the same class of regression (a future contributor re-adding an Analytics-typed method to `IBankStatementImportRepository`) would again go undetected until the next manual arch-review pass. It is a small, additive test-only change with no production code impact, following an existing four-times-used pattern in the same file.

## Data Schemas

### `BankDailyCount` (new — Bank-owned, in-memory query result, not persisted)
| Field | Type | Description |
|---|---|---|
| `Date` | `DateTime` | Calendar day the count applies to |
| `ImportCount` | `int` | Number of bank statement import rows on that day |
| `TotalItemCount` | `int` | Sum of `ItemCount` across those rows |

### `DailyBankStatementStatistics` (unchanged — Analytics-owned, `Domain/Features/Analytics`)
Same three fields (`Date`, `ImportCount`, `TotalItemCount`). Structurally identical to `BankDailyCount` but a distinct, separately-owned type; populated exclusively by `BankStatementStatisticsSourceAdapter`'s mapping step rather than constructed inside Bank's repository.

Both types are transient in-process query DTOs — no EF entity, no `DbSet<>`, no `*Configuration.cs`, no database migration involved.

### `IBankStatementImportRepository.GetDailyCountsAsync` — request/response shape
**Request (parameters):**
| Parameter | Type | Description |
|---|---|---|
| `startDate` | `DateTime` | Inclusive start of the date range |
| `endDate` | `DateTime` | Inclusive end of the date range |
| `byStatementDate` | `bool` | `true` = group/filter by statement date; `false` = group/filter by import date |
| `cancellationToken` | `CancellationToken` | Optional, defaults to `default` |

**Response:** `IReadOnlyList<BankDailyCount>` — one entry per calendar day that has at least one matching `BankStatements` row in the range; days with zero rows are simply absent (gap-filling is the adapter's responsibility, not the repository's).

### `IBankStatementStatisticsSource.GetDailyStatisticsAsync` (unchanged — Analytics contract, out of scope)
```csharp
Task<IReadOnlyList<DailyBankStatementStatistics>> GetDailyStatisticsAsync(
    DateTime startDate, DateTime endDate, BankStatementDateType dateType,
    CancellationToken cancellationToken = default);
```
Response is gap-filled: exactly one `DailyBankStatementStatistics` row per calendar day in `[startDate, endDate]`, inclusive, including zero-count rows for days absent from the underlying repository result.

### Call graph (data flow) after the change
```
AnalyticsRepository
  -> IBankStatementStatisticsSource.GetDailyStatisticsAsync(startDate, endDate, dateType, ct)   [UNCHANGED]
       -> BankStatementStatisticsSourceAdapter.GetDailyStatisticsAsync
            - normalizes startDate/endDate to UTC                                                [UNCHANGED]
            - calls IBankStatementImportRepository.GetDailyCountsAsync(
                  startDate, endDate, dateType == BankStatementDateType.StatementDate, ct)        [NEW call site]
                 -> BankStatementImportRepository.GetDailyCountsAsync
                      - EF Core group-by on BankStatements (statement date vs import date column
                        selected by byStatementDate)
                      - returns IReadOnlyList<BankDailyCount>                                     [NEW return type]
            - builds resultsByDate dictionary from BankDailyCount rows
            - maps each BankDailyCount -> DailyBankStatementStatistics{Date, ImportCount, TotalItemCount}
            - gap-fills zero-count DailyBankStatementStatistics for missing dates                 [UNCHANGED loop]
       <- IReadOnlyList<DailyBankStatementStatistics>, byte-for-byte equivalent to pre-refactor output
```

No HTTP/REST endpoints, MediatR handlers, or frontend surface are affected. No database schema or migration changes.
