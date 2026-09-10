# Code Review: add-daily-counts-repository-method

## Summary
The commit implements `GetDailyCountsAsync` in `IIssuedInvoiceRepository`/`IssuedInvoiceRepository` and adds the 5 specified xUnit tests plus the `MakeInvoiceForDailyCounts` helper, matching the task file's verbatim code character-for-character. The dependent types `DailyInvoiceCount` and `ImportDateType` already exist in `Anela.Heblo.Domain.Features.Analytics` with the exact shape the code assumes, the adapter was correctly left untouched, and the diff is scoped to exactly the 3 specified files.

## Review Result: PASS

### task: add-daily-counts-repository-method
**Status:** PASS

## Docs to Update
(none)

## Overall Notes
- Verified via `git show 8eb9ffc --stat`: exactly 3 files changed (`IIssuedInvoiceRepository.cs`, `IssuedInvoiceRepository.cs`, `IssuedInvoiceRepositoryTests.cs`), 242 insertions, 0 deletions — matches spec's Step 6 scoping requirement.
- Diffed the interface and repository implementation against the task file's exact code blocks: byte-for-byte match, including the XML doc comment, the UTC→Unspecified conversion, the two `GroupBy` branches (`InvoiceDate` vs `LastSyncTime.HasValue` filter), and the gap-fill loop.
- Diffed the test file: the `MakeInvoiceForDailyCounts` helper and all 5 `[Fact]` methods (`InvoiceDateBranch`, `SyncTimeBranch...IgnoresInvoicesWithNullSyncTime`, `EmptyRange...ZeroCountsForEveryDay`, `InclusiveBoundaries...`, `GapFill...`) match the spec verbatim, including the new `using Anela.Heblo.Domain.Features.Analytics;` and `using FluentAssertions;` additions.
- Confirmed `grep -c "InvoiceImportStatisticsSourceAdapter"` against the commit diff returns 0 — the adapter (and `InvoicesModule.cs`, `IInvoiceImportStatisticsSource.cs`) are untouched, consistent with "wired up in the next task."
- Confirmed `DailyInvoiceCount` (`Date: DateTime`, `Count: int`) and `ImportDateType` (`InvoiceDate`, `LastSyncTime`) exist in `backend/src/Anela.Heblo.Domain/Features/Analytics/` with the exact members the new code and tests reference.
- Logic sanity-check against the 5 test expectations: .NET `DateTime` comparison/equality ignores `Kind` (only `Ticks` matter), so the Utc→Unspecified re-tagging used for the EF query does not break matching against the Utc-tagged fixture data in tests; the gap-fill loop correctly walks the inclusive `[startDate.Date, endDate.Date]` range tagging results `DateTimeKind.Utc`; the `LastSyncTime` branch correctly excludes null-sync invoices via `.HasValue`; dictionary lookups key off `.Date` so time-of-day components don't cause gap-fill misses. No off-by-one, kind-conversion, or wrong-grouping-key bugs found — matches the 5 tests' assertions (branch grouping, null-filtering, empty-range zero-fill, inclusive boundaries, gap-fill ordering).
- Take the developer's reported "17/17 passing" test run at face value per instructions; nothing in the code read here contradicts it.
