### task: remove-unused-classification-history-methods

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/IClassificationHistoryRepository.cs:1-18`
- Modify: `backend/src/Anela.Heblo.Persistence/InvoiceClassification/ClassificationHistoryRepository.cs:22-39`
- Test (read-only verification, no edit needed): `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/InvoiceClassificationServiceTests.cs`
- Test (read-only verification, no edit needed): `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/ClassificationHistoryRepositoryTests.cs`

**Goal:** Delete the unused `GetHistoryAsync` and `GetHistoryByInvoiceIdAsync` members from `IClassificationHistoryRepository` and their implementations from `ClassificationHistoryRepository`, leaving `AddAsync` and `GetPagedHistoryAsync` byte-for-byte unchanged.

- [ ] Step 1: In `backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/IClassificationHistoryRepository.cs`, replace the full file contents with:

  ```csharp
  namespace Anela.Heblo.Domain.Features.InvoiceClassification;

  public interface IClassificationHistoryRepository
  {
      Task<ClassificationHistory> AddAsync(ClassificationHistory history);

      Task<(List<ClassificationHistory> Items, int TotalCount)> GetPagedHistoryAsync(
          int page = 1,
          int pageSize = 20,
          DateTime? fromDate = null,
          DateTime? toDate = null,
          string? invoiceNumber = null,
          string? companyName = null);
  }
  ```

  This removes lines 7 (`Task<List<ClassificationHistory>> GetHistoryAsync(int skip = 0, int take = 50);`), 8 (blank), and 9 (`Task<List<ClassificationHistory>> GetHistoryByInvoiceIdAsync(string abraInvoiceId);`) from the original file, and collapses the surrounding blank lines so exactly one blank line separates `AddAsync` from `GetPagedHistoryAsync` (matching the original's one-blank-line-between-members style).

- [ ] Step 2: In `backend/src/Anela.Heblo.Persistence/InvoiceClassification/ClassificationHistoryRepository.cs`, replace the full file contents with:

  ```csharp
  using Microsoft.EntityFrameworkCore;
  using Anela.Heblo.Domain.Features.InvoiceClassification;

  namespace Anela.Heblo.Persistence.InvoiceClassification;

  public class ClassificationHistoryRepository : IClassificationHistoryRepository
  {
      private readonly ApplicationDbContext _context;

      public ClassificationHistoryRepository(ApplicationDbContext context)
      {
          _context = context;
      }

      public async Task<ClassificationHistory> AddAsync(ClassificationHistory history)
      {
          _context.ClassificationHistory.Add(history);
          await _context.SaveChangesAsync();
          return history;
      }

      public async Task<(List<ClassificationHistory> Items, int TotalCount)> GetPagedHistoryAsync(
          int page = 1,
          int pageSize = 20,
          DateTime? fromDate = null,
          DateTime? toDate = null,
          string? invoiceNumber = null,
          string? companyName = null)
      {
          var query = _context.ClassificationHistory
              .Include(h => h.ClassificationRule)
              .AsQueryable();

          // Apply filters
          if (fromDate.HasValue)
              query = query.Where(h => h.Timestamp >= fromDate.Value);

          if (toDate.HasValue)
          {
              // Include the full end day: toDate is sent as midnight (00:00:00), so we extend to the start of the next day
              var endOfDay = toDate.Value.Date.AddDays(1);
              query = query.Where(h => h.Timestamp < endOfDay);
          }

          if (!string.IsNullOrEmpty(invoiceNumber))
              query = query.Where(h => h.AbraInvoiceId.Contains(invoiceNumber));

          if (!string.IsNullOrEmpty(companyName))
              query = query.Where(h => h.CompanyName.Contains(companyName));

          var totalCount = await query.CountAsync();

          var items = await query
              .OrderByDescending(h => h.Timestamp)
              .Skip((page - 1) * pageSize)
              .Take(pageSize)
              .ToListAsync();

          return (items, totalCount);
      }
  }
  ```

  This removes the `GetHistoryAsync` method (original lines 22-30) and the `GetHistoryByInvoiceIdAsync` method (original lines 32-39) in full, along with their surrounding blank lines, leaving exactly one blank line between the remaining `AddAsync` and `GetPagedHistoryAsync` methods. The `using Microsoft.EntityFrameworkCore;` directive stays because `GetPagedHistoryAsync` still uses `.Where`, `.Skip`, `.Take`, `.CountAsync`, `.OrderByDescending`, `.ToListAsync`, `.Include`, and `.AsQueryable` from it.

- [ ] Step 3: Verify no remaining references anywhere in `backend/` to the removed method names on this interface by running:
  `grep -rn "GetHistoryAsync\|GetHistoryByInvoiceIdAsync" backend/ --include=*.cs`
  Expected result: the only matches are the four unrelated occurrences in `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Manufacture/FlexiManufactureHistoryClientTests.cs` (a different `GetHistoryAsync` method on an unrelated Flexi client class — not `IClassificationHistoryRepository`). No matches should exist in `IClassificationHistoryRepository.cs` or `ClassificationHistoryRepository.cs` anymore, and no matches should appear in any InvoiceClassification test file.

- [ ] Step 4: Build the backend solution: `dotnet build Anela.Heblo.sln` (run from the repo root `/home/user/worktrees/feature-3544-Arch-Review-Invoiceclassification-Two-Unused-Metho`). Expected result: build succeeds with 0 errors — this confirms no other file references the removed methods, since C# is statically typed and any lingering reference would be a compile error.

- [ ] Step 5: Run `dotnet format` on the solution (per CLAUDE.md validation requirements): `dotnet format Anela.Heblo.sln`. Expected result: completes without reporting unexpected diffs beyond whitespace already normalized in Steps 1-2; re-check that `IClassificationHistoryRepository.cs` and `ClassificationHistoryRepository.cs` still match the exact content specified in Steps 1 and 2 (no unrelated reformatting was introduced elsewhere).

- [ ] Step 6: Run the InvoiceClassification unit tests: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~InvoiceClassification"`. Expected result: all tests in `InvoiceClassificationServiceTests.cs` and `ClassificationHistoryRepositoryTests.cs` pass, with no compile errors from the Moq mock of `IClassificationHistoryRepository` (it only ever stubs `AddAsync`, which still exists).

- [ ] Step 7: Run the full `Anela.Heblo.Tests` project to confirm no other suite was affected: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`. Expected result: all tests pass (no new failures compared to the pre-change baseline).

- [ ] Step 8: Commit the change:
  ```
  git add backend/src/Anela.Heblo.Domain/Features/InvoiceClassification/IClassificationHistoryRepository.cs backend/src/Anela.Heblo.Persistence/InvoiceClassification/ClassificationHistoryRepository.cs
  git commit -m "Remove unused GetHistoryAsync and GetHistoryByInvoiceIdAsync from IClassificationHistoryRepository"
  ```
