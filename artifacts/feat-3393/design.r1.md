# Design: Refactor BankStatementStatisticsSourceAdapter to Use IBankStatementImportRepository

## Component Design

No new components are introduced. Three existing files are modified, one test file is updated.

### IBankStatementImportRepository (Domain)

Add one method to the existing interface:

```csharp
Task<IReadOnlyList<DailyBankStatementStatistics>> GetDailyStatisticsAsync(
    DateTime startDate,
    DateTime endDate,
    BankStatementDateType dateType,
    CancellationToken cancellationToken = default);
```

Requires adding `using Anela.Heblo.Domain.Features.Analytics;` to the file header (cross-namespace within Domain — acceptable).

### BankStatementImportRepository (Persistence)

Implement `GetDailyStatisticsAsync`. The method body is extracted verbatim from the two branches in `BankStatementStatisticsSourceAdapter` (lines 32–77) and unified:

1. Convert `startDate`/`endDate` from UTC to `DateTimeKind.Unspecified` (persistence concern).
2. Switch on `dateType`:
   - `StatementDate` → filter and group by `b.StatementDate`
   - `ImportDate` → filter and group by `b.ImportDate`
3. Project to `DailyBankStatementStatistics` with `DateTimeKind.Utc` on the date.
4. `AsNoTracking()`.
5. Order ascending by `Date`.

### BankStatementStatisticsSourceAdapter (Application)

Replace:
- Constructor parameter `ApplicationDbContext dbContext` → `IBankStatementImportRepository repository`
- Backing field `_dbContext` → `_repository`
- Remove `using Anela.Heblo.Persistence` and `using Microsoft.EntityFrameworkCore`
- Replace lines 22–77 (two EF Core query blocks) with a single delegation call:
  ```csharp
  var rawStats = await _repository.GetDailyStatisticsAsync(startDate, endDate, dateType, cancellationToken);
  ```
- Retain gap-fill loop (lines 79–103) unchanged.

### BankStatementStatisticsSourceAdapterTests (Test)

Update constructor invocation from:
```csharp
new BankStatementStatisticsSourceAdapter(_context)
```
to:
```csharp
new BankStatementStatisticsSourceAdapter(new BankStatementImportRepository(_context))
```

No test cases, assertions, or seeding logic change.

## Data Schemas

No schema changes. No migrations. The `BankStatements` table is unmodified.

The new method reads from the existing `BankStatements` entity:
- `StatementDate` (DateTime, stored as `timestamp without time zone`)
- `ImportDate` (DateTime, stored as `timestamp without time zone`)
- `ItemCount` (int)
