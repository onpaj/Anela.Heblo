# Remove Analytics dependency from Bank's `IBankStatementImportRepository` — Implementation Plan

**Goal:** Remove the `Anela.Heblo.Domain.Features.Analytics` dependency currently leaking into Bank's
Domain-layer repository interface `IBankStatementImportRepository`. Replace its Analytics-typed
`GetDailyStatisticsAsync(DateTime, DateTime, BankStatementDateType, CancellationToken) : Task<IReadOnlyList<DailyBankStatementStatistics>>`
method with a Bank-owned `GetDailyCountsAsync(DateTime, DateTime, bool byStatementDate, CancellationToken) : Task<IReadOnlyList<BankDailyCount>>`
method. Move the Bank→Analytics projection (and the existing gap-fill behavior) into
`BankStatementStatisticsSourceAdapter`, the Application-layer cross-module seam that already implements
the unchanged Analytics-owned contract `IBankStatementStatisticsSource`. Close the architecture-test gap
that let this violation land undetected by adding a `"Bank (Domain) -> Analytics"` boundary rule.

**Architecture:** Clean Architecture, Vertical Slice modules. `Anela.Heblo.Domain.Features.Bank` must have
zero reference-time dependency on `Anela.Heblo.Domain.Features.Analytics` after this change. The only file
permitted to import both namespaces is `BankStatementStatisticsSourceAdapter` (Application layer) — that is
the designed cross-module adapter seam, per `docs/architecture/development_guidelines.md`
("Cross-Module Communication Example: ILeafletKnowledgeSource"). `IBankStatementStatisticsSource`
(Domain/Features/Analytics) and its method signature are explicitly out of scope and must not change.

**Tech Stack:** C#/.NET 8, EF Core (PostgreSQL via `ApplicationDbContext`), xUnit + FluentAssertions,
EF Core InMemory provider for repository-backed tests.

**Repo root (backend):** `backend/` (solution: `backend/Anela.Heblo.sln`, or run `dotnet build`/`dotnet test`
from `backend/`).

**No database migration, no DI registration change, no frontend/API/MediatR/controller changes** are
required anywhere in this plan — both `BankDailyCount` and `DailyBankStatementStatistics` are transient
in-memory query DTOs (not EF entities, no `DbSet<>`, no `*Configuration.cs`).

---

### task: add-bankdailycount-and-update-domain-contract

**Context:** `IBankStatementImportRepository` is a Domain-layer repository interface owned by the Bank
module, located at `backend/src/Anela.Heblo.Domain/Features/Bank/IBankStatementImportRepository.cs`. It
currently imports `Anela.Heblo.Domain.Features.Analytics` (an Analytics-owned namespace) and declares a
method `GetDailyStatisticsAsync` that takes an Analytics enum (`BankStatementDateType`) and returns a list
of an Analytics type (`DailyBankStatementStatistics`). This violates the project's module-boundary rule:
"No direct access to another module's entities" / "Communication between modules exclusively through
contracts/interfaces" — the Domain layer must not depend on another module's types.

This task fixes the Domain-layer contract only: it introduces a new Bank-owned record `BankDailyCount`
(a same-shape, Analytics-agnostic mirror of `DailyBankStatementStatistics`, with fields `Date` (`DateTime`),
`ImportCount` (`int`), `TotalItemCount` (`int`)), and replaces `GetDailyStatisticsAsync` on
`IBankStatementImportRepository` with a Bank-owned `GetDailyCountsAsync` method that takes a plain
`bool byStatementDate` instead of the Analytics enum, and returns `IReadOnlyList<BankDailyCount>` instead of
the Analytics type. The `using Anela.Heblo.Domain.Features.Analytics;` import is dropped from this file
entirely.

Note: after this task, the solution will NOT compile (the EF Core implementation and the adapter still
reference the old method/types) — that is fixed by the next tasks in this plan. This task's own step 4
build check is expected to show exactly those two pre-existing-reference errors and no others; do not try
to make the whole solution build in this task.

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Bank/BankDailyCount.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/Bank/IBankStatementImportRepository.cs` (currently 32 lines; the whole file is replaced below)

- [ ] Step 1: Create the new Bank-owned record type.

  Create `backend/src/Anela.Heblo.Domain/Features/Bank/BankDailyCount.cs` with exactly this content:

  ```csharp
  namespace Anela.Heblo.Domain.Features.Bank;

  public record BankDailyCount(DateTime Date, int ImportCount, int TotalItemCount);
  ```

- [ ] Step 2: Replace the full contents of `IBankStatementImportRepository.cs`.

  The current full file content (32 lines) is:

  ```csharp
  using Anela.Heblo.Domain.Features.Analytics;

  namespace Anela.Heblo.Domain.Features.Bank;

  public interface IBankStatementImportRepository
  {
      Task<(IEnumerable<BankStatementImport> Items, int TotalCount)> GetFilteredAsync(
          BankStatementListFilter filter,
          int skip = 0,
          int take = 50,
          string orderBy = "ImportDate",
          bool ascending = false,
          CancellationToken cancellationToken = default);

      Task<BankStatementImport?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
      Task<BankStatementImport> AddAsync(BankStatementImport bankStatement, CancellationToken cancellationToken = default);

      Task<IReadOnlyDictionary<string, string>> GetExistingResultsByTransferIdsAsync(
          IReadOnlyCollection<string> transferIds, CancellationToken cancellationToken = default);

      Task<DateTime?> GetMaxStatementDateAsync(string account, CancellationToken cancellationToken = default);

      Task<BankStatementImport?> GetByTransferIdAsync(string transferId, CancellationToken cancellationToken = default);

      Task<BankStatementImport> UpdateAsync(BankStatementImport bankStatement, CancellationToken cancellationToken = default);

      Task<IReadOnlyList<DailyBankStatementStatistics>> GetDailyStatisticsAsync(
          DateTime startDate,
          DateTime endDate,
          BankStatementDateType dateType,
          CancellationToken cancellationToken = default);
  }
  ```

  Replace the entire file with (note: line 1's `using Anela.Heblo.Domain.Features.Analytics;` is removed,
  and the last method is replaced):

  ```csharp
  namespace Anela.Heblo.Domain.Features.Bank;

  public interface IBankStatementImportRepository
  {
      Task<(IEnumerable<BankStatementImport> Items, int TotalCount)> GetFilteredAsync(
          BankStatementListFilter filter,
          int skip = 0,
          int take = 50,
          string orderBy = "ImportDate",
          bool ascending = false,
          CancellationToken cancellationToken = default);

      Task<BankStatementImport?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
      Task<BankStatementImport> AddAsync(BankStatementImport bankStatement, CancellationToken cancellationToken = default);

      Task<IReadOnlyDictionary<string, string>> GetExistingResultsByTransferIdsAsync(
          IReadOnlyCollection<string> transferIds, CancellationToken cancellationToken = default);

      Task<DateTime?> GetMaxStatementDateAsync(string account, CancellationToken cancellationToken = default);

      Task<BankStatementImport?> GetByTransferIdAsync(string transferId, CancellationToken cancellationToken = default);

      Task<BankStatementImport> UpdateAsync(BankStatementImport bankStatement, CancellationToken cancellationToken = default);

      Task<IReadOnlyList<BankDailyCount>> GetDailyCountsAsync(
          DateTime startDate,
          DateTime endDate,
          bool byStatementDate,
          CancellationToken cancellationToken = default);
  }
  ```

- [ ] Step 3: Verify the Analytics import is gone from this file.

  Run:
  ```bash
  grep -n "Anela.Heblo.Domain.Features.Analytics\|DailyBankStatementStatistics\|BankStatementDateType" backend/src/Anela.Heblo.Domain/Features/Bank/IBankStatementImportRepository.cs
  ```
  Expected output: no matches (empty output, exit code 1).

- [ ] Step 4: Confirm the Domain project itself still compiles (the Domain project has no dependency on
  the Persistence/Application projects that will still reference the old method name, so it should build
  cleanly in isolation).

  Run:
  ```bash
  cd backend && dotnet build src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj
  ```
  Expected: `Build succeeded.` with 0 errors. (Do NOT attempt to build the full solution yet — the
  Persistence and Application projects will fail to build until the next two tasks are complete; that is
  expected and out of scope for this task.)

- [ ] Step 5: Commit.

  ```bash
  git add backend/src/Anela.Heblo.Domain/Features/Bank/BankDailyCount.cs backend/src/Anela.Heblo.Domain/Features/Bank/IBankStatementImportRepository.cs
  git commit -m "Bank: replace GetDailyStatisticsAsync with Analytics-agnostic GetDailyCountsAsync on IBankStatementImportRepository"
  ```

---

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

### task: update-adapter-projection-and-boundary-test

**Context:** This task has two independent parts touching different files: (1) updating
`BankStatementStatisticsSourceAdapter` to call the Bank repository's new method and do the
Bank→Analytics type projection itself, and (2) adding a missing architecture-boundary test rule that
would have caught the original violation. Both are small, mechanical, and can be done in either order;
do both as part of this task.

**Part 1 background — the adapter.**
`backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/BankStatementStatisticsSourceAdapter.cs`
implements the Analytics-owned contract `IBankStatementStatisticsSource`
(`backend/src/Anela.Heblo.Domain/Features/Analytics/IBankStatementStatisticsSource.cs` — this file and its
method signature are OUT OF SCOPE, do not touch it) via its public method
`GetDailyStatisticsAsync(DateTime, DateTime, BankStatementDateType, CancellationToken) : Task<IReadOnlyList<DailyBankStatementStatistics>>`.
That public signature must NOT change. Only the method's internal implementation changes: it must now call
the Bank repository's `GetDailyCountsAsync` (already updated by a separate, already-completed task to
return `IReadOnlyList<BankDailyCount>` given a `bool byStatementDate` parameter instead of the Analytics
enum), then map each returned `BankDailyCount` to a `DailyBankStatementStatistics { Date, ImportCount,
TotalItemCount }` instance, preserving the existing gap-fill loop unchanged (every date in
`[startDate, endDate]` inclusive that is absent from the repository result gets a zero-count
`DailyBankStatementStatistics` row).

The Bank-owned `BankDailyCount` record (already added by a separate, already-completed task) is:
```csharp
namespace Anela.Heblo.Domain.Features.Bank;

public record BankDailyCount(DateTime Date, int ImportCount, int TotalItemCount);
```

The Bank repository interface's new method (already updated by a separate, already-completed task) is:
```csharp
Task<IReadOnlyList<BankDailyCount>> GetDailyCountsAsync(
    DateTime startDate,
    DateTime endDate,
    bool byStatementDate,
    CancellationToken cancellationToken = default);
```

The current full content of
`backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/BankStatementStatisticsSourceAdapter.cs`
(55 lines) is:

```csharp
using Anela.Heblo.Domain.Features.Analytics;
using Anela.Heblo.Domain.Features.Bank;

namespace Anela.Heblo.Application.Features.Bank.Infrastructure;

internal sealed class BankStatementStatisticsSourceAdapter : IBankStatementStatisticsSource
{
    private readonly IBankStatementImportRepository _repository;

    public BankStatementStatisticsSourceAdapter(IBankStatementImportRepository repository)
    {
        _repository = repository;
    }

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

        var results = await _repository.GetDailyStatisticsAsync(startDate, endDate, dateType, cancellationToken);

        var resultsByDate = results.ToDictionary(r => r.Date.Date);
        var filledResults = new List<DailyBankStatementStatistics>();
        var currentDate = DateTime.SpecifyKind(startDate.Date, DateTimeKind.Utc);
        var endDateOnly = DateTime.SpecifyKind(endDate.Date, DateTimeKind.Utc);

        while (currentDate <= endDateOnly)
        {
            if (resultsByDate.TryGetValue(currentDate.Date, out var existingResult))
            {
                filledResults.Add(existingResult);
            }
            else
            {
                filledResults.Add(new DailyBankStatementStatistics
                {
                    Date = currentDate,
                    ImportCount = 0,
                    TotalItemCount = 0
                });
            }

            currentDate = currentDate.AddDays(1);
        }

        return filledResults;
    }
}
```

Note that `IBankStatementStatisticsSource.GetDailyStatisticsAsync` (the interface member this class
implements) keeps its Analytics-typed signature (`BankStatementDateType` parameter,
`DailyBankStatementStatistics` return type) — that is the unchanged, out-of-scope Analytics contract. Only
the *body* of this method, and its *internal* call to `_repository`, change.

**Part 2 background — the boundary test.**
`backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` has a `TheoryData<ModuleBoundaryRule>`
data source (`Rules()`, starting at line 341) that already contains an `"Analytics (Domain) -> Bank"` rule
(forbidding Analytics from referencing Bank types) at lines 477-487:

```csharp
        new ModuleBoundaryRule(
            Name: "Analytics (Domain) -> Bank",
            InspectedNamespacePrefix: "Anela.Heblo.Domain.Features.Analytics",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.Bank",
                "Anela.Heblo.Application.Features.Bank",
                "Anela.Heblo.Persistence.Bank",
            },
            Allowlist: new HashSet<string>(StringComparer.Ordinal),
            InspectedAssembly: "Anela.Heblo.Domain"),
```

There is no rule in the reverse direction (`Bank (Domain) -> Analytics`) — exactly the direction the
original violation occurred in, which is why `dotnet build` and the existing test suite did not catch it.
This task adds that missing rule immediately after the existing `"Analytics (Domain) -> Bank"` entry (i.e.
immediately before the `"Catalog -> Logistics"` entry that currently follows it at line 489), following the
exact same `ModuleBoundaryRule` pattern.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/BankStatementStatisticsSourceAdapter.cs` (full file, 55 lines)
- Modify: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs:477-489` (insert new rule between the existing `"Analytics (Domain) -> Bank"` entry and the `"Catalog -> Logistics"` entry)
- Test: `backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementStatisticsSourceAdapterTests.cs` (run only, do not edit)
- Test: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` (run, including the new rule)

- [ ] Step 1: Replace the full contents of `BankStatementStatisticsSourceAdapter.cs` with:

  ```csharp
  using Anela.Heblo.Domain.Features.Analytics;
  using Anela.Heblo.Domain.Features.Bank;

  namespace Anela.Heblo.Application.Features.Bank.Infrastructure;

  internal sealed class BankStatementStatisticsSourceAdapter : IBankStatementStatisticsSource
  {
      private readonly IBankStatementImportRepository _repository;

      public BankStatementStatisticsSourceAdapter(IBankStatementImportRepository repository)
      {
          _repository = repository;
      }

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

          var counts = await _repository.GetDailyCountsAsync(
              startDate, endDate, dateType == BankStatementDateType.StatementDate, cancellationToken);

          var results = counts
              .Select(c => new DailyBankStatementStatistics
              {
                  Date = c.Date,
                  ImportCount = c.ImportCount,
                  TotalItemCount = c.TotalItemCount
              })
              .ToList();

          var resultsByDate = results.ToDictionary(r => r.Date.Date);
          var filledResults = new List<DailyBankStatementStatistics>();
          var currentDate = DateTime.SpecifyKind(startDate.Date, DateTimeKind.Utc);
          var endDateOnly = DateTime.SpecifyKind(endDate.Date, DateTimeKind.Utc);

          while (currentDate <= endDateOnly)
          {
              if (resultsByDate.TryGetValue(currentDate.Date, out var existingResult))
              {
                  filledResults.Add(existingResult);
              }
              else
              {
                  filledResults.Add(new DailyBankStatementStatistics
                  {
                      Date = currentDate,
                      ImportCount = 0,
                      TotalItemCount = 0
                  });
              }

              currentDate = currentDate.AddDays(1);
          }

          return filledResults;
      }
  }
  ```

  Notes on this change:
  - `_repository.GetDailyStatisticsAsync(startDate, endDate, dateType, cancellationToken)` is replaced by
    `_repository.GetDailyCountsAsync(startDate, endDate, dateType == BankStatementDateType.StatementDate, cancellationToken)`.
  - A new mapping step (`counts.Select(c => new DailyBankStatementStatistics { ... })`) converts
    `IReadOnlyList<BankDailyCount>` to `List<DailyBankStatementStatistics>` before the existing
    dictionary-build + gap-fill loop, which is otherwise byte-for-byte unchanged from before.
  - The public method signature (`GetDailyStatisticsAsync(DateTime, DateTime, BankStatementDateType, CancellationToken) : Task<IReadOnlyList<DailyBankStatementStatistics>>`)
    and the `using Anela.Heblo.Domain.Features.Analytics;` / `using Anela.Heblo.Domain.Features.Bank;`
    imports are unchanged — this class legitimately needs both namespaces since it is the designed
    cross-module seam.

- [ ] Step 2: Add the missing `"Bank (Domain) -> Analytics"` boundary rule.

  In `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`, find the existing
  `"Analytics (Domain) -> Bank"` entry (currently lines 477-487):

  ```csharp
          new ModuleBoundaryRule(
              Name: "Analytics (Domain) -> Bank",
              InspectedNamespacePrefix: "Anela.Heblo.Domain.Features.Analytics",
              ForbiddenNamespacePrefixes: new[]
              {
                  "Anela.Heblo.Domain.Features.Bank",
                  "Anela.Heblo.Application.Features.Bank",
                  "Anela.Heblo.Persistence.Bank",
              },
              Allowlist: new HashSet<string>(StringComparer.Ordinal),
              InspectedAssembly: "Anela.Heblo.Domain"),

          new ModuleBoundaryRule(
              Name: "Catalog -> Logistics",
  ```

  Insert a new `ModuleBoundaryRule` entry immediately after the `"Analytics (Domain) -> Bank"` entry's
  closing `),` and before the `"Catalog -> Logistics"` entry, so the block becomes:

  ```csharp
          new ModuleBoundaryRule(
              Name: "Analytics (Domain) -> Bank",
              InspectedNamespacePrefix: "Anela.Heblo.Domain.Features.Analytics",
              ForbiddenNamespacePrefixes: new[]
              {
                  "Anela.Heblo.Domain.Features.Bank",
                  "Anela.Heblo.Application.Features.Bank",
                  "Anela.Heblo.Persistence.Bank",
              },
              Allowlist: new HashSet<string>(StringComparer.Ordinal),
              InspectedAssembly: "Anela.Heblo.Domain"),

          new ModuleBoundaryRule(
              Name: "Bank (Domain) -> Analytics",
              InspectedNamespacePrefix: "Anela.Heblo.Domain.Features.Bank",
              ForbiddenNamespacePrefixes: new[]
              {
                  "Anela.Heblo.Domain.Features.Analytics",
                  "Anela.Heblo.Application.Features.Analytics",
                  "Anela.Heblo.Persistence.Analytics",
              },
              Allowlist: new HashSet<string>(StringComparer.Ordinal),
              InspectedAssembly: "Anela.Heblo.Domain"),

          new ModuleBoundaryRule(
              Name: "Catalog -> Logistics",
  ```

  This mirrors the existing `"Analytics (Domain) -> Bank"` rule exactly, but inspects
  `Anela.Heblo.Domain.Features.Bank` types and forbids them from referencing any
  `Anela.Heblo.Domain.Features.Analytics` / `Anela.Heblo.Application.Features.Analytics` /
  `Anela.Heblo.Persistence.Analytics` type, with an empty allowlist (no known/permitted violations after
  this refactor) and `InspectedAssembly: "Anela.Heblo.Domain"` (since `IBankStatementImportRepository` and
  `BankDailyCount` live in the Domain assembly).

- [ ] Step 3: Build the full solution.

  Run:
  ```bash
  cd backend && dotnet build
  ```
  Expected: `Build succeeded.` with 0 errors, 0 warnings related to this change. This confirms the Domain,
  Persistence, and Application projects are now all mutually consistent with the new
  `GetDailyCountsAsync`/`BankDailyCount` signature.

- [ ] Step 4: Run `dotnet format` (per project validation requirements) to confirm no formatting drift.

  Run:
  ```bash
  cd backend && dotnet format --verify-no-changes
  ```
  Expected: exits 0 with no reported formatting issues. If it reports issues in files touched by this
  plan, run `dotnet format` (without `--verify-no-changes`) and re-check the diff only touches
  whitespace/formatting in the files this plan modified.

- [ ] Step 5: Run the adapter test file to confirm it passes unmodified (per spec FR-4 acceptance
  criterion and Out of Scope: "no test code changes required in this file").

  Run:
  ```bash
  cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~BankStatementStatisticsSourceAdapterTests"
  ```
  Expected: all 5 tests pass —
  `GetDailyStatisticsAsync_StatementDateBranch_ReturnsCountsAndSummedItemCount`,
  `GetDailyStatisticsAsync_ImportDateBranch_ReturnsCountsAndSummedItemCount`,
  `GetDailyStatisticsAsync_EmptyRange_ReturnsZeroRowsForEveryDay`,
  `GetDailyStatisticsAsync_InclusiveBoundaries_IncludesStatementsOnStartAndEndDate`,
  `GetDailyStatisticsAsync_GapFill_EmitsZeroRowsForMissingDays`.
  `Passed! - Failed: 0, Passed: 5, Skipped: 0`.

  If any of these 5 tests fail, do NOT edit this test file to make it pass — per spec FR-4 and the Out of
  Scope section, this file must pass unmodified. A failure here means the adapter's mapping/gap-fill logic
  in Step 1 diverged from the original behavior; re-check Step 1 against the "Notes on this change" above.

- [ ] Step 6: Run the architecture boundary test suite to confirm the new rule passes (no existing
  violation) and the whole theory (all existing rules) still passes.

  Run:
  ```bash
  cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ModuleBoundariesTests"
  ```
  Expected: all tests pass, including a new theory case for `"Bank (Domain) -> Analytics"` under
  `Consumer_types_should_not_reference_provider_owned_namespaces`. `Passed! - Failed: 0`.

  If the new `"Bank (Domain) -> Analytics"` rule fails, it means some type under
  `Anela.Heblo.Domain.Features.Bank` still references an Analytics namespace — re-check that
  `IBankStatementImportRepository.cs` and `BankDailyCount.cs` (from the earlier tasks in this plan) have no
  remaining Analytics references, per their own verification steps.

- [ ] Step 7: Run the full Bank test suite (per the arch-review's risk mitigation: "run the full Bank test
  suite, not just the adapter test, before calling this done") to confirm no other Bank test regressed.

  Run:
  ```bash
  cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Features.Bank"
  ```
  Expected: all tests pass (`Failed: 0`), including
  `ImportBankStatementHandlerTests`, `GetBankStatementListHandlerTests`, `GetBankStatementByIdHandlerTests`,
  `Infrastructure.Jobs.ComgateCzkImportJobTests`, `Infrastructure.Jobs.ComgateEurImportJobTests`,
  `Infrastructure.Jobs.ShoptetPayImportJobTests`, `Infrastructure.Jobs.BankImportJobBaseTests`, and
  `BankStatementStatisticsSourceAdapterTests` (already verified in Step 5). None of these files reference
  `GetDailyStatisticsAsync`/`GetDailyCountsAsync` on the repository directly (verified by grep during
  planning), so none should need code changes — a failure here would indicate an unexpected regression to
  investigate, not an expected required edit.

- [ ] Step 8: Run the full backend test suite as a final safety net.

  Run:
  ```bash
  cd backend && dotnet test
  ```
  Expected: `Failed: 0` across the whole suite.

- [ ] Step 9: Commit.

  ```bash
  git add backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/BankStatementStatisticsSourceAdapter.cs backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
  git commit -m "Bank: adapter projects BankDailyCount to DailyBankStatementStatistics; add Bank(Domain)->Analytics boundary rule"
  ```

---

## Self-review notes (for the planner, not a task)

- Spec coverage: FR-1 (BankDailyCount) -> task `add-bankdailycount-and-update-domain-contract` Step 1.
  FR-2 (interface replace) -> same task, Step 2. FR-3 (EF impl) -> task
  `update-bankstatementimportrepository-ef-implementation`, Steps 1-2. FR-4 (adapter projection + gap-fill
  preserved) -> task `update-adapter-projection-and-boundary-test`, Step 1. FR-5 (boundary rule,
  recommended by arch-review) -> same task, Step 2. NFR-3 (zero Bank Domain -> Analytics dependency,
  verifiable) -> enforced by the new rule added in Step 2 and verified in Step 6; also spot-checked via
  grep in Steps 3 of the first two tasks.
- Out-of-scope items respected: `IBankStatementStatisticsSource.cs` untouched, `AnalyticsRepository.cs`
  untouched, `BankStatementStatisticsSourceAdapterTests.cs` untouched (run-only), no migration, no DI
  change, no frontend/API changes.
- Task self-containment: each task repeats the full current-state code it depends on (interface shape,
  record shape, method bodies) rather than referencing "Task N", since each is handed to an isolated
  developer subagent with no visibility into the others.
