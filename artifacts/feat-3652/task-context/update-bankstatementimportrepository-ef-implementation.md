### task: update-bankstatementimportrepository-ef-implementation

**Context:** `BankStatementImportRepository` (EF Core implementation of `IBankStatementImportRepository`)
lives at `backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportRepository.cs`. The Domain
interface `IBankStatementImportRepository` (in `backend/src/Anela.Heblo.Domain/Features/Bank/`) has been
changed (by a separate, already-completed task) to declare:

```csharp
Task<IReadOnlyList<BankDailyCount>> GetDailyCountsAsync(
    DateTime startDate,
    DateTime endDate,
    bool byStatementDate,
    CancellationToken cancellationToken = default);
```

instead of the old:

```csharp
Task<IReadOnlyList<DailyBankStatementStatistics>> GetDailyStatisticsAsync(
    DateTime startDate,
    DateTime endDate,
    BankStatementDateType dateType,
    CancellationToken cancellationToken = default);
```

`BankDailyCount` is a new Bank-owned record already added at
`backend/src/Anela.Heblo.Domain/Features/Bank/BankDailyCount.cs`:
```csharp
namespace Anela.Heblo.Domain.Features.Bank;

public record BankDailyCount(DateTime Date, int ImportCount, int TotalItemCount);
```

This task updates the EF Core implementation to match the new interface signature: branch on the
`bool byStatementDate` parameter instead of switching on the Analytics enum `BankStatementDateType`, keep
the exact same grouping/aggregation logic (group `_context.BankStatements` by Year/Month/Day of the
selected date column, `ImportCount` = row count, `TotalItemCount` = `Sum(ItemCount)`), and return
`BankDailyCount` instances instead of `DailyBankStatementStatistics`. Also drop the
`using Anela.Heblo.Domain.Features.Analytics;` import from this file, since it is no longer needed once
the enum/type references are gone.

The current full content of `backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportRepository.cs`
is 199 lines. Only the top `using` block (lines 1-3) and the `GetDailyStatisticsAsync` method (lines
143-195) change; everything else in the file (lines 4-142 and 196-199) is unchanged.

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportRepository.cs:1-3` (using block)
- Modify: `backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportRepository.cs:143-195` (method body)
- Test: `backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementStatisticsSourceAdapterTests.cs` (run only, do not edit — see Step 4)

- [ ] Step 1: Update the `using` block at the top of the file.

  Current (lines 1-3):
  ```csharp
  using Anela.Heblo.Domain.Features.Analytics;
  using Anela.Heblo.Domain.Features.Bank;
  using Microsoft.EntityFrameworkCore;
  ```

  Replace with:
  ```csharp
  using Anela.Heblo.Domain.Features.Bank;
  using Microsoft.EntityFrameworkCore;
  ```

- [ ] Step 2: Replace the `GetDailyStatisticsAsync` method (current lines 143-195) with a retyped
  `GetDailyCountsAsync` method.

  Current method (lines 143-195):
  ```csharp
      public async Task<IReadOnlyList<DailyBankStatementStatistics>> GetDailyStatisticsAsync(
          DateTime startDate,
          DateTime endDate,
          BankStatementDateType dateType,
          CancellationToken cancellationToken = default)
      {
          var startUnspecified = DateTime.SpecifyKind(startDate, DateTimeKind.Unspecified);
          var endUnspecified = DateTime.SpecifyKind(endDate, DateTimeKind.Unspecified);

          var rawResults = dateType switch
          {
              BankStatementDateType.StatementDate => await _context.BankStatements
                  .AsNoTracking()
                  .Where(b => b.StatementDate >= startUnspecified && b.StatementDate <= endUnspecified)
                  .GroupBy(b => new { b.StatementDate.Year, b.StatementDate.Month, b.StatementDate.Day })
                  .Select(g => new
                  {
                      g.Key.Year,
                      g.Key.Month,
                      g.Key.Day,
                      ImportCount = g.Count(),
                      TotalItemCount = g.Sum(b => b.ItemCount)
                  })
                  .OrderBy(d => new DateTime(d.Year, d.Month, d.Day))
                  .ToListAsync(cancellationToken),

              BankStatementDateType.ImportDate => await _context.BankStatements
                  .AsNoTracking()
                  .Where(b => b.ImportDate >= startUnspecified && b.ImportDate <= endUnspecified)
                  .GroupBy(b => new { b.ImportDate.Year, b.ImportDate.Month, b.ImportDate.Day })
                  .Select(g => new
                  {
                      g.Key.Year,
                      g.Key.Month,
                      g.Key.Day,
                      ImportCount = g.Count(),
                      TotalItemCount = g.Sum(b => b.ItemCount)
                  })
                  .OrderBy(d => new DateTime(d.Year, d.Month, d.Day))
                  .ToListAsync(cancellationToken),

              _ => throw new ArgumentOutOfRangeException(nameof(dateType), dateType, null)
          };

          return rawResults
              .Select(r => new DailyBankStatementStatistics
              {
                  Date = DateTime.SpecifyKind(new DateTime(r.Year, r.Month, r.Day), DateTimeKind.Utc),
                  ImportCount = r.ImportCount,
                  TotalItemCount = r.TotalItemCount
              })
              .ToList();
      }
  ```

  Replace with:
  ```csharp
      public async Task<IReadOnlyList<BankDailyCount>> GetDailyCountsAsync(
          DateTime startDate,
          DateTime endDate,
          bool byStatementDate,
          CancellationToken cancellationToken = default)
      {
          var startUnspecified = DateTime.SpecifyKind(startDate, DateTimeKind.Unspecified);
          var endUnspecified = DateTime.SpecifyKind(endDate, DateTimeKind.Unspecified);

          var rawResults = byStatementDate
              ? await _context.BankStatements
                  .AsNoTracking()
                  .Where(b => b.StatementDate >= startUnspecified && b.StatementDate <= endUnspecified)
                  .GroupBy(b => new { b.StatementDate.Year, b.StatementDate.Month, b.StatementDate.Day })
                  .Select(g => new
                  {
                      g.Key.Year,
                      g.Key.Month,
                      g.Key.Day,
                      ImportCount = g.Count(),
                      TotalItemCount = g.Sum(b => b.ItemCount)
                  })
                  .OrderBy(d => new DateTime(d.Year, d.Month, d.Day))
                  .ToListAsync(cancellationToken)
              : await _context.BankStatements
                  .AsNoTracking()
                  .Where(b => b.ImportDate >= startUnspecified && b.ImportDate <= endUnspecified)
                  .GroupBy(b => new { b.ImportDate.Year, b.ImportDate.Month, b.ImportDate.Day })
                  .Select(g => new
                  {
                      g.Key.Year,
                      g.Key.Month,
                      g.Key.Day,
                      ImportCount = g.Count(),
                      TotalItemCount = g.Sum(b => b.ItemCount)
                  })
                  .OrderBy(d => new DateTime(d.Year, d.Month, d.Day))
                  .ToListAsync(cancellationToken);

          return rawResults
              .Select(r => new BankDailyCount(
                  DateTime.SpecifyKind(new DateTime(r.Year, r.Month, r.Day), DateTimeKind.Utc),
                  r.ImportCount,
                  r.TotalItemCount))
              .ToList();
      }
  ```

  This preserves identical grouping/filtering/aggregation semantics: `byStatementDate == true` behaves
  exactly like the old `BankStatementDateType.StatementDate` branch; `byStatementDate == false` behaves
  exactly like the old `BankStatementDateType.ImportDate` branch. No `DailyBankStatementStatistics` or
  `BankStatementDateType` reference remains in this file.

- [ ] Step 3: Verify no Analytics reference remains in this file.

  Run:
  ```bash
  grep -n "Anela.Heblo.Domain.Features.Analytics\|DailyBankStatementStatistics\|BankStatementDateType" backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportRepository.cs
  ```
  Expected output: no matches (empty output, exit code 1).

- [ ] Step 4: Build the Persistence project. This will still fail to build the full solution/tests until
  the adapter task is also complete (the adapter references the old method name), but the Persistence
  project itself must build cleanly since it now matches the (already-updated) Domain interface.

  Run:
  ```bash
  cd backend && dotnet build src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj
  ```
  Expected: `Build succeeded.` with 0 errors.

  Note: `dotnet test` for `BankStatementStatisticsSourceAdapterTests.cs` will NOT pass yet at this point
  in isolation if run before the adapter task — the Application project (containing
  `BankStatementStatisticsSourceAdapter`) still calls the old `GetDailyStatisticsAsync` method name and
  will fail to compile until that task's changes are applied. Do not attempt to run the test suite as part
  of this task; that verification happens in the adapter task.

- [ ] Step 5: Commit.

  ```bash
  git add backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportRepository.cs
  git commit -m "Bank: retype BankStatementImportRepository.GetDailyStatisticsAsync to GetDailyCountsAsync returning BankDailyCount"
  ```

---

