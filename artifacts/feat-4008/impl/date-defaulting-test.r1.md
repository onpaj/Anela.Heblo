# Implementation: date-defaulting-test

## What was implemented
Added a new `[Fact]` test, `Handle_BothDatesNull_DefaultsToTrailing30DayWindow`, to the existing
`GetIssuedInvoiceSyncStatsHandlerTests` class. It covers spec FR-1: when both `FromDate` and
`ToDate` on `GetIssuedInvoiceSyncStatsRequest` are `null`, the handler must default the queried
window to the trailing 30 days (`DateTime.Now.Date.AddDays(-30)` through `DateTime.Now.Date`) and
pass those exact dates through to `IIssuedInvoiceRepository.GetSyncStatsAsync`. The test uses
`It.Is<DateTime>(d => d.Date == expected...)` predicates on both arguments (comparing `.Date` only,
per arch-review Decision 1/2, to avoid a spurious failure from time-of-day drift between the
expected-value computation in the test and inside the handler) and verifies the call happened
exactly once, plus asserts `response.Success` is `true`.

Verified against the actual handler source
(`backend/src/Anela.Heblo.Application/Features/Invoices/UseCases/GetIssuedInvoiceSyncStats/GetIssuedInvoiceSyncStatsHandler.cs`)
before writing the test — the handler's null-coalescing logic (`request.FromDate ?? DateTime.Now.Date.AddDays(-30)`,
`request.ToDate ?? DateTime.Now.Date`) matches the test's expectations exactly, and the existing
test file's mock/handler field names (`_repositoryMock`, `_handler`) and constructor already matched
the task spec's snippet verbatim, so no adaptation was needed beyond appending the method.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceSyncStatsHandlerTests.cs` — appended the `Handle_BothDatesNull_DefaultsToTrailing30DayWindow` test method inside the existing test class, after the constructor.

## Tests
- `backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceSyncStatsHandlerTests.cs::Handle_BothDatesNull_DefaultsToTrailing30DayWindow` — covers FR-1 (null `FromDate`/`ToDate` default to a trailing 30-day window ending today, and those exact dates are passed to the repository call).

## How to verify
```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetIssuedInvoiceSyncStatsHandlerTests.Handle_BothDatesNull_DefaultsToTrailing30DayWindow"
```
Result observed: `Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1`.

## Notes
No deviations from the task spec were needed — the existing test file's class/field/method names
matched the spec's snippet exactly (`_repositoryMock`, `_handler`,
`GetIssuedInvoiceSyncStatsRequest`, `IssuedInvoiceSyncStats`), and `ImplicitUsings` is enabled in
the test project so no additional `using System;` was required for `DateTime`. The test run took
noticeably longer than usual (~13 minutes wall clock, mostly in `dotnet build`/restore) due to
environment resource constraints in this sandbox; this is unrelated to the change itself, and the
final `Passed!` output confirms correctness. Did not run a full `dotnet build`/`dotnet format`
pass across the whole solution, since the task scope is a single test-only addition with no
production code change; the targeted test build succeeded cleanly (only pre-existing nullable
warnings from unrelated files appeared in the build log).

## PR Summary
Adds the missing unit test for `GetIssuedInvoiceSyncStatsHandler`'s date-range defaulting behavior
(FR-1), closing the coverage gap called out for issue #4008: when both `FromDate` and `ToDate` are
omitted from the request, the handler must query the trailing 30 days ending today, and this test
now pins that behavior down with an exact date-only predicate on both arguments passed to
`IIssuedInvoiceRepository.GetSyncStatsAsync`, guarding against a sign flip or wrong date source
silently shifting the reported window.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceSyncStatsHandlerTests.cs` — added `Handle_BothDatesNull_DefaultsToTrailing30DayWindow` test

## Status
DONE
