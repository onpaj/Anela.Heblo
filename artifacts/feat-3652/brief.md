## Module
Bank

## Finding
`IBankStatementImportRepository` lives in `backend/src/Anela.Heblo.Domain/Features/Bank/IBankStatementImportRepository.cs` — the innermost layer of the Bank module. Line 1 imports `Anela.Heblo.Domain.Features.Analytics`, and the method added for the cross-module adapter leaks those types into the repository contract:

```csharp
// IBankStatementImportRepository.cs — lines 1, 27-31
using Anela.Heblo.Domain.Features.Analytics;   // ← Analytics domain, not Bank

public interface IBankStatementImportRepository
{
    // ... Bank-specific methods ...
    Task<IReadOnlyList<DailyBankStatementStatistics>> GetDailyStatisticsAsync(
        DateTime startDate,
        DateTime endDate,
        BankStatementDateType dateType,          // ← Analytics enum
        CancellationToken cancellationToken = default);
}
```

`DailyBankStatementStatistics` and `BankStatementDateType` are owned by `Anela.Heblo.Domain.Features.Analytics`.

The adapter `BankStatementStatisticsSourceAdapter` (`backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/BankStatementStatisticsSourceAdapter.cs`) correctly implements `IBankStatementStatisticsSource` (Analytics-owned contract) and delegates to `_repository.GetDailyStatisticsAsync`. The cross-module adapter pattern itself is correct, but to service it, `GetDailyStatisticsAsync` was added to the Bank domain repository interface — pulling Analytics' types into Bank's Domain layer.

## Why it matters
The development guidelines state: "No direct access to another module's entities" and "Communication between modules exclusively through contracts/interfaces." The Domain layer must not depend on another module's types; that rule applies as strictly to repository interfaces as to entities.

The concrete consequence: the Bank module's domain layer cannot be compiled, tested, or deployed without the Analytics module. If `DailyBankStatementStatistics` or `BankStatementDateType` change shape, Bank's domain interface breaks even though Bank has no business-level dependency on Analytics.

## Suggested fix
Remove `GetDailyStatisticsAsync` from `IBankStatementImportRepository`. Introduce a narrow, Analytics-agnostic repository method that returns raw per-day counts in Bank-owned types, and let the adapter do the projection to `DailyBankStatementStatistics`.

**Option A — add a Bank-typed query method and keep the adapter thin (preferred):**
```csharp
// New in Domain/Features/Bank/ (no Analytics import needed):
public record BankDailyCount(DateTime Date, int ImportCount, int TotalItemCount);

// IBankStatementImportRepository — replace GetDailyStatisticsAsync:
Task<IReadOnlyList<BankDailyCount>> GetDailyCountsAsync(
    DateTime startDate, DateTime endDate, bool byStatementDate,
    CancellationToken cancellationToken = default);
```

```csharp
// BankStatementStatisticsSourceAdapter — maps BankDailyCount → DailyBankStatementStatistics:
var counts = await _repository.GetDailyCountsAsync(startDate, endDate,
    dateType == BankStatementDateType.StatementDate, cancellationToken);
// fill sparse days and project to DailyBankStatementStatistics as before
```

**Option B — move the query out of the domain repository entirely:** Introduce an Analytics-aware internal repository (`IBankDailyStatisticsQuery`) in the Application layer's Infrastructure folder, visible only to the adapter, never exposed in the Domain layer.

Either option removes the Analytics dependency from Bank's domain contract.

## Reconnaissance notes (verified against current source, not just the issue text)

Current `IBankStatementImportRepository.GetDailyStatisticsAsync` signature (Domain/Features/Bank/IBankStatementImportRepository.cs:27-31) — unabridged, confirmed by direct file read:
```csharp
Task<IReadOnlyList<DailyBankStatementStatistics>> GetDailyStatisticsAsync(
    DateTime startDate,
    DateTime endDate,
    BankStatementDateType dateType,
    CancellationToken cancellationToken = default);
```

The concrete EF Core implementation is `BankStatementImportRepository.GetDailyStatisticsAsync`
(`backend/src/Anela.Heblo.Persistence/Features/Bank/BankStatementImportRepository.cs:143-195`). It
switches on `dateType` (`BankStatementDateType.StatementDate` vs `.ImportDate`) to pick which date
column to group by, groups `_context.BankStatements` by Year/Month/Day, projects `ImportCount` /
`TotalItemCount = Sum(ItemCount)`, then maps to `DailyBankStatementStatistics`. Under Option A this
becomes `GetDailyCountsAsync(startDate, endDate, bool byStatementDate, ct)`, switching on the bool
instead of the Analytics enum, and returning `IReadOnlyList<BankDailyCount>` (raw rows, no
`DailyBankStatementStatistics` construction — that projection moves to the adapter). Also drop the
`using Anela.Heblo.Domain.Features.Analytics;` import from both this file and the Domain interface
file once `BankStatementDateType`/`DailyBankStatementStatistics` are no longer referenced there.

The adapter is `BankStatementStatisticsSourceAdapter`
(`backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/BankStatementStatisticsSourceAdapter.cs`).
It implements the **Analytics-owned** contract `IBankStatementStatisticsSource.GetDailyStatisticsAsync`
(`backend/src/Anela.Heblo.Domain/Features/Analytics/IBankStatementStatisticsSource.cs`) — that
Analytics-side interface and its method name/signature are **out of scope and must not change**.
Today the adapter just forwards to `_repository.GetDailyStatisticsAsync(...)` and then gap-fills
missing dates with zero-count `DailyBankStatementStatistics` rows. After the fix, the adapter must:
1. Call `_repository.GetDailyCountsAsync(startDate, endDate, dateType == BankStatementDateType.StatementDate, cancellationToken)`.
2. Map each returned `BankDailyCount` to `DailyBankStatementStatistics { Date, ImportCount, TotalItemCount }`.
3. Keep the existing gap-fill loop (fills every date in `[startDate, endDate]` with a zero row when absent) — behavior must be unchanged.

`DailyBankStatementStatistics` already has exactly the fields `Date`, `ImportCount`, `TotalItemCount`
(Analytics domain type) — so `BankDailyCount` is a same-shape, Bank-owned mirror of it, not a new
concept.

Test impact verified by direct grep — only these files reference `GetDailyStatisticsAsync` anywhere
in the codebase:
- `Domain/Features/Analytics/IBankStatementStatisticsSource.cs` — Analytics contract, **do not touch**.
- `Domain/Features/Bank/IBankStatementImportRepository.cs` — rename/retype per Option A.
- `Persistence/Features/Analytics/AnalyticsRepository.cs` — calls
  `_bankStatementStatisticsSource.GetDailyStatisticsAsync(...)` (the Analytics-owned interface
  method) — **unaffected**, do not touch.
- `Persistence/Features/Bank/BankStatementImportRepository.cs` — the EF implementation to rename/retype.
- `Application/Features/Bank/Infrastructure/BankStatementStatisticsSourceAdapter.cs` — update the
  call site + mapping as above.
- `test/Anela.Heblo.Tests/Features/Bank/BankStatementStatisticsSourceAdapterTests.cs` — exercises the
  adapter end-to-end against a real EF Core in-memory `BankStatementImportRepository` (no mocking of
  the repository interface), calling only `_adapter.GetDailyStatisticsAsync(...)` (the unchanged
  Analytics-facing adapter method). No signature changes needed in this test file; it should keep
  passing unmodified once the adapter's internal wiring is updated. Test method names may still say
  "GetDailyStatisticsAsync" — that's the adapter's own (unchanged) method name, not the repository's.

No other test file in `backend/test/` sets up a mock/stub for
`IBankStatementImportRepository.GetDailyStatisticsAsync` specifically (checked: `ImportBankStatementHandlerTests.cs`,
`GetBankStatementListHandlerTests.cs`, `Infrastructure/Jobs/*Tests.cs`, `GetBankStatementByIdHandlerTests.cs`
reference the interface for other methods only) — the interface rename should not require touching them,
but the developer/reviewer should still run the full Bank test suite to confirm.

## Acceptance criteria
- `IBankStatementImportRepository` (Domain/Features/Bank) no longer imports
  `Anela.Heblo.Domain.Features.Analytics` and no longer references `DailyBankStatementStatistics` or
  `BankStatementDateType`.
- A new Bank-owned `BankDailyCount` record (or equivalent) carries the raw per-day counts.
- `BankStatementStatisticsSourceAdapter` still satisfies `IBankStatementStatisticsSource` unchanged and
  produces identical `DailyBankStatementStatistics` output (including the zero-fill-gaps behavior) as
  before the refactor.
- `dotnet build` succeeds; the Bank and Analytics test suites pass unmodified in behavior (test code may
  need to compile against the renamed repository method only where it is directly invoked, per the
  grep above).

---
_Filed by daily arch-review routine on 2026-07-15. Issue: https://github.com/onpaj/Anela.Heblo/issues/3652_
