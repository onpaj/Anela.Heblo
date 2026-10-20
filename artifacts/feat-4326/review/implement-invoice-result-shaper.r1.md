# Code Review: implement-invoice-result-shaper

## Summary
The implementation matches the task context's specified interface, class, and test file
exactly (interface shape, `CanHandle` predicate, `ShapeAsync` behavior, and all 6 test cases).
Build, the targeted test class, and the full `~DataQuality` filter all pass. The developer also
fixed an unrelated pre-existing compile error in `RecurringJobSeeder.cs` that was blocking the
whole `Anela.Heblo.Application` project from building; this is out of this task's stated scope
but was necessary to verify anything at all and is flagged clearly for a human to confirm.

## Review Result: PASS

### task: implement-invoice-result-shaper
**Status:** PASS

Verified:
- `IDqtResultShaper` interface signature (`CanHandle(DqtTestType)`, `ShapeAsync(DqtRun run,
  GetDqtRunDetailResponse response, int page, int pageSize, CancellationToken ct = default)`)
  matches the task context verbatim, and mirrors the existing `IDqtJobRunner.CanHandle`
  pattern used elsewhere in this module (architecture consistency).
- `InvoiceDqtResultShaper.CanHandle` returns true only for `DqtTestType.IssuedInvoiceComparison`.
- `InvoiceDqtResultShaper.ShapeAsync` maps `run.Results` to `List<InvoiceDqtResultDto>` via
  `IMapper` and assigns to `response.Results`, without touching `DriftResults` /
  `TotalDriftResults` — confirmed an existing `CreateMap<InvoiceDqtResult,
  InvoiceDqtResultDto>()` profile entry already covers this mapping.
- `InvoiceDqtResultShaperTests` contains exactly the theory (5 cases) + fact test specified,
  and both pass: `Passed! - Failed: 0, Passed: 6, Total: 6`.
- `GetDqtRunDetailHandler` and `DataQualityModule` are correctly left untouched, per the task
  plan's stated split across the three tasks (this task is additive-only).
- `dotnet test --filter FullyQualifiedName~DataQuality` → 149/149 passing on the successful
  run. `dotnet format --verify-no-changes` on all changed files reports no violations.

Note on the unrelated `RecurringJobSeeder.cs` one-line fix: `HasSeededFieldsChanged(existing,
config)` → `HasSeededFieldsChanged(existingConfig, config)`. This is a genuine, obvious
argument-mismatch bug (passing a `List<RecurringJobConfiguration>` where the method's
declared parameter is a single `RecurringJobConfiguration`) that produced CS1503 and made the
*entire* `Anela.Heblo.Application` project fail to build — i.e., it blocked verifying not just
this task but every task in this feature and, in principle, any other work touching this
project. `git merge-base --is-ancestor 882659fe origin/main` confirms the bug already exists on
`origin/main` (commit 882659fe), so this is not something introduced by this feature branch.
Given it is a one-line, unambiguous, already-broken-on-main fix required to get any build
signal at all, and is called out explicitly rather than silently folded in, this does not block
PASS on this task — but a human should confirm the fix and separately investigate why a
build-breaking error reached `main`.

Note on the reported flaky test: `RunDqtHandlerTests.Handle_InvoiceTestType_
InvokesMatchingRunnerOnly` failed once under the full `~DataQuality` filter with a
self-contradictory Moq message (verify says "0 times" while its own invocation log shows
`RunAsync` called once), then passed on an immediate re-run of the same filter, and passed
every time run in isolation. This class is unrelated to `GetDqtRunDetailHandler` /
`IDqtResultShaper` (it tests `RunDqtHandler` / `IDqtJobRunner` instead) and this task does not
modify any file it depends on. Consistent with a pre-existing test-parallelism/ordering flake,
not a regression from this change. Not treated as a correctness bug in this task's scope, but
worth a human noting for the test suite's health.

## Docs to Update
(None — this task adds an internal interface/service with no public-facing behavior change and
no new concept beyond what a later task in the same plan will expose.)

## Overall Notes
No correctness, spec-compliance, or architecture issues found. The two flagged items above
(unrelated build-blocker fix, and a pre-existing flaky test observed but not caused by this
change) are informational for a human, not blockers for this task.
