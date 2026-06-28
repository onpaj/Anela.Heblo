### task: extract-daily-statistics-into-repository

**Goal**

Move the two EF Core LINQ query blocks from `BankStatementStatisticsSourceAdapter` into a new `GetDailyStatisticsAsync` method on `IBankStatementImportRepository` and its implementation, then update the adapter to delegate to the repository and update the test constructor accordingly.

**Context**

The adapter (`BankStatementStatisticsSourceAdapter`) currently holds:
- A direct `ApplicationDbContext` dependency from the Persistence layer
- Two near-identical EF Core query branches (one for `StatementDate`, one for `ImportDate`)
- UTC → `DateTimeKind.Unspecified` conversion before the query
- A gap-fill loop after the query

The gap-fill loop is _presentation/application logic_ and stays in the adapter. The UTC conversion and the LINQ queries are _persistence concerns_ and move to the repository.

The test already exercises the full `GetDailyStatisticsAsync` contract on the adapter level (5 test cases covering both date type branches, gap-fill, boundary inclusion, and empty range). After the refactor, the test constructs `BankStatementStatisticsSourceAdapter` with a real `BankStatementImportRepository` wrapping the in-memory context, so all test assertions remain valid without change.

**Files to create/modify**

| Action | File |
|--------|------|
| Modify | `backend/src/Anela.Heblo.Domain/Features/Bank/IBankStatementImportRepository.cs` |
| Modify | `backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportRepository.cs` |
| Modify | `backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/BankStatementStatisticsSourceAdapter.cs` |
| Modify | `backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementStatisticsSourceAdapterTests.cs` |

**Implementation steps**

1. **Add the method to the interface** (`IBankStatementImportRepository.cs`).

   Add a `using` directive for the Analytics namespace and append the new method signature:

   ```csharp
   using Anela.Heblo.Domain.Features.Analytics;

   // ... existing namespace/interface declaration ...

   Task<IReadOnlyList<DailyBankStatementStatistics>> GetDailyStatisticsAsync(
       DateTime startDate,
       DateTime endDate,
       BankStatementDateType dateType,
       CancellationToken cancellationToken = default);
   ```

   The `using Anela.Heblo.Domain.Features.Analytics;` directive is a cross-namespace reference within the same Domain assembly — this is acceptable per arch review.

2. **Implement the method** (`BankStatementImportRepository.cs`).

   Add the following method to the class. The method must:
   - Convert `startDate`/`endDate` from UTC to `DateTimeKind.Unspecified` (PostgreSQL does not store timezone; EF Core requires `Unspecified` for date comparisons).
   - Switch on `dateType` to select the correct date column.
   - Group by `Year`/`Month`/`Day` (EF Core cannot group by `DateTime.Date` directly).
   - Project to `DailyBankStatementStatistics` with `DateTimeKind.Utc` applied to the `Date` field.
   - Use `AsNoTracking()`.
   - Return results ordered ascending by `Date`.

   ```csharp
   public async Task<IReadOnlyList<DailyBankStatementStatistics>> GetDailyStatisticsAsync(
       DateTime startDate,
       DateTime endDate,
       BankStatementDateType dateType,
       CancellationToken cancellationToken = default)
   {
       var startUnspecified = DateTime.SpecifyKind(startDate, DateTimeKind.Unspecified);
       var endUnspecified   = DateTime.SpecifyKind(endDate,   DateTimeKind.Unspecified);

       var rawResults = dateType switch
       {
           BankStatementDateType.StatementDate => await _context.BankStatements
               .AsNoTracking()
               .Where(b => b.StatementDate >= startUnspecified && b.StatementDate <= endUnspecified)
               .GroupBy(b => new { b.StatementDate.Year, b.StatementDate.Month, b.StatementDate.Day })
               .Select(g => new
               {
                   g.Key.Year, g.Key.Month, g.Key.Day,
                   ImportCount    = g.Count(),
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
                   g.Key.Year, g.Key.Month, g.Key.Day,
                   ImportCount    = g.Count(),
                   TotalItemCount = g.Sum(b => b.ItemCount)
               })
               .OrderBy(d => new DateTime(d.Year, d.Month, d.Day))
               .ToListAsync(cancellationToken),

           _ => throw new ArgumentOutOfRangeException(nameof(dateType), dateType, null)
       };

       return rawResults
           .Select(r => new DailyBankStatementStatistics
           {
               Date           = DateTime.SpecifyKind(new DateTime(r.Year, r.Month, r.Day), DateTimeKind.Utc),
               ImportCount    = r.ImportCount,
               TotalItemCount = r.TotalItemCount
           })
           .ToList();
   }
   ```

   Add the required `using` directive at the top of the file:
   ```csharp
   using Anela.Heblo.Domain.Features.Analytics;
   ```

3. **Refactor the adapter** (`BankStatementStatisticsSourceAdapter.cs`).

   - Replace the constructor parameter `ApplicationDbContext dbContext` with `IBankStatementImportRepository repository`.
   - Replace the field `private readonly ApplicationDbContext _dbContext` with `private readonly IBankStatementImportRepository _repository`.
   - Remove the `using Anela.Heblo.Persistence;` and `using Microsoft.EntityFrameworkCore;` directives.
   - Replace the entire if/else query block (including the UTC normalisation at the top) with a single repository call:

     ```csharp
     var results = await _repository.GetDailyStatisticsAsync(
         startDate, endDate, dateType, cancellationToken);
     ```

   - Keep the UTC normalisation guard at the top of the method (`if (startDate.Kind != DateTimeKind.Utc)` / `if (endDate.Kind != DateTimeKind.Utc)`), the `resultsByDate` dictionary build, and the gap-fill loop exactly as they are — those are application-layer concerns.

   After the refactor the method body should look like:

   ```csharp
   public async Task<IReadOnlyList<DailyBankStatementStatistics>> GetDailyStatisticsAsync(
       DateTime startDate,
       DateTime endDate,
       BankStatementDateType dateType,
       CancellationToken cancellationToken = default)
   {
       if (startDate.Kind != DateTimeKind.Utc)
           startDate = startDate.ToUniversalTime();
       if (endDate.Kind != DateTimeKind.Utc)
           endDate = endDate.ToUniversalTime();

       var results = await _repository.GetDailyStatisticsAsync(
           startDate, endDate, dateType, cancellationToken);

       var resultsByDate = results.ToDictionary(r => r.Date.Date);
       var filledResults = new List<DailyBankStatementStatistics>();
       var currentDate   = DateTime.SpecifyKind(startDate.Date, DateTimeKind.Utc);
       var endDateOnly   = DateTime.SpecifyKind(endDate.Date,   DateTimeKind.Utc);

       while (currentDate <= endDateOnly)
       {
           if (resultsByDate.TryGetValue(currentDate.Date, out var existingResult))
               filledResults.Add(existingResult);
           else
               filledResults.Add(new DailyBankStatementStatistics
               {
                   Date           = currentDate,
                   ImportCount    = 0,
                   TotalItemCount = 0
               });

           currentDate = currentDate.AddDays(1);
       }

       return filledResults;
   }
   ```

4. **Update the test constructor** (`BankStatementStatisticsSourceAdapterTests.cs`).

   In the constructor, change the adapter construction line from:

   ```csharp
   _adapter = new BankStatementStatisticsSourceAdapter(_context);
   ```

   to:

   ```csharp
   _adapter = new BankStatementStatisticsSourceAdapter(new BankStatementImportRepository(_context));
   ```

   Also remove the `using Anela.Heblo.Persistence;` directive only if no other symbol in the test file requires it — `ApplicationDbContext` is still used directly, so the directive must stay. Add `using Anela.Heblo.Persistence.Features.Bank;` if it is not already present (needed for `BankStatementImportRepository`).

5. **Verify the build**

   From the repo root:
   ```
   dotnet build backend/Anela.Heblo.sln
   dotnet format backend/Anela.Heblo.sln --verify-no-changes
   ```

   Then run the affected test class:
   ```
   dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
       --filter "FullyQualifiedName~BankStatementStatisticsSourceAdapterTests"
   ```

**Tests to write**

No new tests. The five existing tests in `BankStatementStatisticsSourceAdapterTests.cs` already provide full coverage of the new repository method because the test wires a real `BankStatementImportRepository` over an in-memory `ApplicationDbContext`. The test cases cover:

- `GetDailyStatisticsAsync_StatementDateBranch_ReturnsCountsAndSummedItemCount`
- `GetDailyStatisticsAsync_ImportDateBranch_ReturnsCountsAndSummedItemCount`
- `GetDailyStatisticsAsync_EmptyRange_ReturnsZeroRowsForEveryDay`
- `GetDailyStatisticsAsync_InclusiveBoundaries_IncludesStatementsOnStartAndEndDate`
- `GetDailyStatisticsAsync_GapFill_EmitsZeroRowsForMissingDays`

**Acceptance criteria**

- [ ] `IBankStatementImportRepository` declares `GetDailyStatisticsAsync` with the exact signature from spec FR-1.
- [ ] `BankStatementImportRepository` implements the method; it uses `AsNoTracking()`, converts UTC to `DateTimeKind.Unspecified` before querying, projects to `DailyBankStatementStatistics` with `DateTimeKind.Utc` on `Date`, and returns results ordered ascending.
- [ ] `BankStatementStatisticsSourceAdapter` has no references to `ApplicationDbContext`, `Microsoft.EntityFrameworkCore`, or `Anela.Heblo.Persistence` namespaces; its constructor accepts `IBankStatementImportRepository`.
- [ ] The test constructor passes `new BankStatementImportRepository(_context)` to the adapter.
- [ ] `dotnet build` exits with code 0 — no compilation errors or warnings introduced.
- [ ] `dotnet format --verify-no-changes` exits with code 0.
- [ ] All five tests in `BankStatementStatisticsSourceAdapterTests` pass.
