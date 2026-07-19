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

