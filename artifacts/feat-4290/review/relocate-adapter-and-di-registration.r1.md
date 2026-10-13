# Code Review: relocate-adapter-and-di-registration

## Summary
The adapter was moved from `Anela.Heblo.Persistence.Marketing` to
`Anela.Heblo.Persistence.Invoices` via `git mv`, and its DI registration was
relocated from `MarketingPerformanceModule.cs` to `InvoicesModule.cs`,
matching the provider-owns-adapter pattern used elsewhere in that file. One
necessary, well-justified deviation from the task-context text (keeping
`using Anela.Heblo.Persistence.Marketing;` in `MarketingPerformanceModule.cs`
because `MarketingPerformanceRepository` — an unrelated, same-module type —
also lives in that namespace) was required to make the build succeed; the
deviation is documented in the impl artifact and does not affect the
cross-module fix this task targets.

## Review Result: PASS

### task: relocate-adapter-and-di-registration
**Status:** PASS

Verified:
- `git mv` used (rename preserved, confirmed via `git diff --staged`).
- Moved file's only changes are the namespace line and the new adapter
  comment block — class body byte-for-byte identical to spec.
- `MarketingPerformanceModule.cs`: registration line removed; the
  now-unused-per-spec `using Anela.Heblo.Persistence.Marketing;` was kept
  because `MarketingPerformanceRepository` (registered on the line directly
  above) is also in that namespace — removing it broke the build with
  `CS0246`. This is a same-module reference, not a re-introduction of the
  cross-module violation being fixed, so it does not compromise FR-1/FR-2.
- `InvoicesModule.cs`: `using Anela.Heblo.Domain.Features.MarketingPerformance;`
  added; registration added in the correct position (after
  `IInvoiceImportStatisticsSource`, before the DataQuality adapters) with a
  comment matching the established style.
- Test file: only the `using` changed, exactly as specified; class name,
  namespace, and test bodies untouched.
- `dotnet build`: `0 Error(s)`; `163 Warning(s)`, none in the four touched
  files (verified by inspecting the full warning list — all warnings are
  pre-existing, unrelated files).
- `dotnet test --filter "FullyQualifiedName~IssuedInvoiceMonthlyRevenueSourceTests|FullyQualifiedName~MarketingPerformanceRefreshServiceTests"`:
  `Passed! - Failed: 0, Passed: 12, Skipped: 0, Total: 12`.
- No files outside the four listed in the task-context were touched (confirmed
  via `git status --porcelain=v1 --untracked-files=all`).

## Docs to Update
(none — this is an internal module-boundary fix with no public behavior, CLI,
or config change)

## Overall Notes
Pure relocation, no behavior change. The one deviation from the literal task
text was required for the build to succeed and is the correct call: the spec
author's Step 3 diff apparently didn't account for `MarketingPerformanceRepository`
sharing the `Anela.Heblo.Persistence.Marketing` namespace with the adapter
being moved.
