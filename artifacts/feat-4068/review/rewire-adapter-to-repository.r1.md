# Code Review: rewire-adapter-to-repository

## Summary
The implementation is exactly what the spec asked for: `InvoiceImportStatisticsSourceAdapter` is now a thin, sealed pass-through injecting `IIssuedInvoiceRepository` and delegating `GetDailyCountsAsync` verbatim, with no remaining reference to `ApplicationDbContext`, EF Core, or `Anela.Heblo.Persistence`. The unit tests were rewritten to mock the repository, following the `InvoiceConsumptionSourceAdapterTests` pattern precisely, and only the two files named in the task's file list were touched. All claims in the developer's report were independently reproduced.

## Review Result: PASS

### task: rewire-adapter-to-repository
**Status:** PASS

## Docs to Update
- `backend/src/Anela.Heblo.Application/Features/Invoices/InvoicesModule.cs` (line ~45) — the comment above `services.AddScoped<IInvoiceImportStatisticsSource, InvoiceImportStatisticsSourceAdapter>()` still reads "Scoped because the adapter wraps ApplicationDbContext (also Scoped)", which is now stale (the adapter wraps `IIssuedInvoiceRepository`, not `ApplicationDbContext`). The developer correctly identified this and left it untouched since the file wasn't in this task's scope — flagging it here per the review criteria (informational only, not blocking).

## Overall Notes
Verified independently, not just from the developer's report:
- Read `InvoiceImportStatisticsSourceAdapter.cs` directly: constructor takes only `IIssuedInvoiceRepository`, body is a single delegating `return _repository.GetDailyCountsAsync(...)` call, no persistence-layer imports.
- Read `IIssuedInvoiceRepository.cs`: `GetDailyCountsAsync(DateTime, DateTime, ImportDateType, CancellationToken = default)` signature matches the adapter and the `IInvoiceImportStatisticsSource` contract exactly.
- Read `IssuedInvoiceRepository.cs` (Persistence layer): confirms the prior task (`add-daily-counts-repository-method`) already implemented the EF Core query there, so the grouping/gap-fill logic that used to live in the adapter's tests is now correctly the repository's responsibility, not lost.
- Compared the new test file against `InvoiceConsumptionSourceAdapterTests.cs`: same `Mock<IIssuedInvoiceRepository>` + `CreateAdapter()` factory pattern; the 3 new tests cover argument forwarding, unchanged-result pass-through, and correct `ImportDateType` forwarding for both enum values — reasonable coverage for a pure delegator.
- Ran `grep -n "ApplicationDbContext\|EntityFrameworkCore\|Anela.Heblo.Persistence" .../InvoiceImportStatisticsSourceAdapter.cs` myself: no matches, confirming the architecture-boundary fix.
- Ran `dotnet test .../Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~InvoiceImportStatisticsSourceAdapterTests" -c Release` myself: `Passed! - Failed: 0, Passed: 3, Skipped: 0, Total: 3` — matches the developer's report exactly.
- Ran `dotnet build Anela.Heblo.sln -c Release` myself from repo root: `0 Error(s)` — confirms no other caller (e.g. `AnalyticsRepository.cs`) depends on the removed constructor shape, and DI registration in `InvoicesModule.cs` resolves the new constructor without changes (container is unaware of parameter types by name, so no registration edit was needed).
- Confirmed via `git show --stat HEAD` that only the two files in the task's file list were modified — fully surgical, no scope creep.
- Independently confirmed the developer's build-hang note is plausible and irrelevant to code correctness: the `-c Release` workaround is an invocation flag only, no source/project changes were made to work around it, and my own `-c Release` runs reproduced their reported results exactly.

No architecture, correctness, or completeness issues found.

**Status:** PASS
