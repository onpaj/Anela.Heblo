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

