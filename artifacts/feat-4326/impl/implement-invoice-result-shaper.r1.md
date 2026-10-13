# Implementation: implement-invoice-result-shaper

## What was implemented

Added a new `IDqtResultShaper` interface — the polymorphic seam the task plan uses to
eventually replace `GetDqtRunDetailHandler`'s `if (run.TestType == ...)` branches — plus its
first concrete implementation, `InvoiceDqtResultShaper`, which handles
`DqtTestType.IssuedInvoiceComparison` by mapping `run.Results` onto
`GetDqtRunDetailResponse.Results` via `IMapper`. This mirrors the existing
`IDqtJobRunner.CanHandle(DqtTestType)` pattern used by `RunDqtHandler`. Per this task's scope,
`GetDqtRunDetailHandler` itself and `DataQualityModule` DI registration are **not** touched —
those are the `rewire-handler-and-register-shapers` task.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/IDqtResultShaper.cs` —
  new interface: `bool CanHandle(DqtTestType)` + `Task ShapeAsync(DqtRun, GetDqtRunDetailResponse, int page, int pageSize, CancellationToken)`.
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/InvoiceDqtResultShaper.cs` —
  new class implementing `IDqtResultShaper`; `CanHandle` returns true only for
  `DqtTestType.IssuedInvoiceComparison`; `ShapeAsync` maps `run.Results` to
  `List<InvoiceDqtResultDto>` and assigns it to `response.Results`, leaving
  `DriftResults`/`TotalDriftResults` untouched.
- `backend/test/Anela.Heblo.Tests/Features/DataQuality/InvoiceDqtResultShaperTests.cs` — new
  test file (written first, per TDD step in the task context).

### Unrelated pre-existing build-blocker fixed (flagged for human review)

- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs`
  (1 line) — `HasSeededFieldsChanged(existing, config)` passed the whole
  `List<RecurringJobConfiguration>` (`existing`) where the method's signature expects a single
  `RecurringJobConfiguration`; this was a **compile error (CS1503)** that broke the build of
  the entire `Anela.Heblo.Application` project. Confirmed via `git log`/`git merge-base
  --is-ancestor` that this was already merged into `origin/main` at commit `882659fe`
  ("#4318: BackgroundJobs — RecurringJobSeeder must not overwrite audit fields when nothing
  changed") — it predates and is completely unrelated to this DataQuality feature. Without
  this one-line fix (`existing` → `existingConfig`, the local var already resolved for exactly
  this comparison a few lines above), nothing in the `Anela.Heblo.Application` project — not
  just this task's new files — could be built or tested at all. Fixed as the minimal possible
  change to unblock verification; a human should confirm this fix is correct and separately
  track why it wasn't caught before merging to main.

## Tests

- `InvoiceDqtResultShaperTests`:
  - `CanHandle_ReturnsTrueOnlyForIssuedInvoiceComparison` (`[Theory]`, 5 cases) — true only for
    `IssuedInvoiceComparison`, false for all 4 drift `DqtTestType` values.
  - `ShapeAsync_MapsRunResultsOntoResponse_AndLeavesDriftFieldsUntouched` (`[Fact]`) — verifies
    the mapper's output is assigned to `response.Results` (`Assert.Same`), and that
    `DriftResults` stays `null` / `TotalDriftResults` stays `0`.

## How to verify

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~InvoiceDqtResultShaperTests
# Passed! - Failed: 0, Passed: 6, Skipped: 0, Total: 6

dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter FullyQualifiedName~DataQuality
# Passed! - Failed: 0, Passed: 149, Skipped: 0, Total: 149
```

Note: one run of the full `~DataQuality` filter showed
`RunDqtHandlerTests.Handle_InvoiceTestType_InvokesMatchingRunnerOnly` failing with a Moq
"expected once, was 0 times" message that contradicted its own "Performed invocations" log (it
listed `RunAsync` as having been called once). Re-running the exact same filter immediately
after passed 149/149, and running `~RunDqtHandlerTests` alone passed 8/8 every time. This is a
pre-existing, order/parallelism-dependent flake in a different handler's test class
(`RunDqtHandler`, not `GetDqtRunDetailHandler`) that this task's new files never touch or
reference — not a regression from this change.

```bash
cd .. # repo root
dotnet format Anela.Heblo.sln --verify-no-changes --include \
  backend/src/Anela.Heblo.Application/Features/DataQuality/Services/IDqtResultShaper.cs \
  backend/src/Anela.Heblo.Application/Features/DataQuality/Services/InvoiceDqtResultShaper.cs \
  backend/test/Anela.Heblo.Tests/Features/DataQuality/InvoiceDqtResultShaperTests.cs \
  backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs
# no output -> no violations
```

## Notes

- Followed the task context's code verbatim (interface, implementation, and test file all
  match the plan's snippets exactly) — this was a mechanical, single-purpose task with a
  complete spec, so no design judgment calls were needed beyond the pre-existing build-blocker
  above.
- `InvoiceDqtResultDto` already had an `AutoMapper` map
  (`CreateMap<InvoiceDqtResult, InvoiceDqtResultDto>()` in `DataQualityMappingProfile.cs`), so
  no mapping-profile changes were needed.
- `GetDqtRunDetailHandler` and `DataQualityModule` DI wiring are deliberately left untouched —
  `InvoiceDqtResultShaper` is not yet registered in DI and not yet called by the handler. That
  is explicitly the `rewire-handler-and-register-shapers` task's job, per the task plan.

## PR Summary
Adds `IDqtResultShaper` (the `CanHandle`/`ShapeAsync` seam that `GetDqtRunDetailHandler` will
later dispatch through, mirroring the existing `IDqtJobRunner` pattern) and its first
implementation, `InvoiceDqtResultShaper`, covering the invoice-comparison test type. The
handler itself is not modified yet — that happens in a later task once the drift-comparison
shaper also exists. Also includes an unrelated one-line fix to `RecurringJobSeeder.cs` that
was required to unblock the build (see "Unrelated pre-existing build-blocker fixed" above).

### Changes
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/IDqtResultShaper.cs` — new interface
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/InvoiceDqtResultShaper.cs` — new implementation
- `backend/test/Anela.Heblo.Tests/Features/DataQuality/InvoiceDqtResultShaperTests.cs` — new tests
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs` — unrelated 1-line build-blocker fix

## Status
DONE_WITH_CONCERNS
