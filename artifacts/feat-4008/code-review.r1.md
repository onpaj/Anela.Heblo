# Code Review: feat-4008 — Coverage gap: Invoices GetIssuedInvoiceSyncStatsHandler

## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- None

## Scope of the diff

The entire feature diff (`git diff` against merge-base with `main`) touches
exactly one production/test source file:

- `backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceSyncStatsHandlerTests.cs`
  (new file, 150 lines, 4 `[Fact]` tests) — 100% additive, no deletions or
  modifications elsewhere.

Everything else in the diff is pipeline artifact bookkeeping under
`artifacts/feat-4008/` (spec, task plan, per-task impl/review notes,
`state.json`) — no production code under `backend/src/` was touched, matching
NFR-2 ("No production code changes") in `spec.r1.md`.

## What was verified

- Read `GetIssuedInvoiceSyncStatsHandler.cs`, `GetIssuedInvoiceSyncStatsRequest.cs`,
  `GetIssuedInvoiceSyncStatsResponse.cs`, `IssuedInvoiceSyncStats.cs`, and
  `IIssuedInvoiceRepository.GetSyncStatsAsync`'s signature directly from the
  worktree (not just the diff) and cross-checked every assertion in the new
  test file against the actual handler logic line-by-line:
  - Date defaulting (`request.FromDate ?? DateTime.Now.Date.AddDays(-30)`,
    `request.ToDate ?? DateTime.Now.Date`) matches
    `Handle_BothDatesNull_DefaultsToTrailing30DayWindow`'s `It.Is<DateTime>(d
    => d.Date == expectedFrom/expectedTo)` predicates exactly — the sign
    (`-30`, not `+30`) and which bound gets `AddDays` are both pinned, so a
    regression here would fail the test. Comparing on `.Date` (not exact
    `DateTime` equality) correctly absorbs the negligible clock-tick gap
    between the test computing its expected value and the handler running,
    per FR-1's acceptance criteria.
  - `Handle_ExplicitDates_PassesThemThroughUnchanged` supplies distinct
    `FromDate`/`ToDate` and asserts the exact (non-defaulted) values reach
    `GetSyncStatsAsync`, matching FR-2.
  - `Handle_RepositoryThrows_ReturnsStructuredFailure` matches the handler's
    `catch (Exception ex)` block exactly: `Success = false`, `ErrorCode =
    ErrorCodes.Exception`, `Params["ErrorMessage"] = "Chyba při načítání
    statistik synchronizace faktur"` (verbatim string match against the
    handler's literal), and asserts every stat field is left at its
    type-default (0 / null) since the handler's catch block never populates
    them — matches FR-3 exactly, including confirming the exception does not
    propagate (the `await` completing and reaching the assertions is itself
    proof of no rethrow).
  - `Handle_RepositoryReturnsStats_MapsAllFieldsOntoResponse` seeds
    `IssuedInvoiceSyncStats` with 5 independently distinguishable values and
    asserts all 7 mapped fields (including the computed `SyncSuccessRate`,
    150/200 → 75m, correctly derived from `IssuedInvoiceSyncStats`'s
    computed-property formula) — a dropped or swapped field in the handler's
    object initializer would fail this test. Matches FR-4.
- Confirmed `IIssuedInvoiceRepository.GetSyncStatsAsync(DateTime fromDate,
  DateTime toDate, CancellationToken cancellationToken = default)`'s
  parameter order/types match every mock `Setup`/`Verify` call in the new
  tests.
- Confirmed test fixture shape (constructor-injected `Mock<IIssuedInvoiceRepository>`
  + `Mock.Of<ILogger<...>>()`, xUnit/Moq/FluentAssertions, AAA structure)
  matches the sibling `GetIssuedInvoiceDetailHandlerTests.cs` pattern the spec
  calls out as the template to follow.
- The four per-task `impl/*.r1.md` and `review/*.r1.md` artifacts each
  independently report the relevant test(s) passing as they were added
  incrementally (1/1, then 3/3, then 4/4), and `impl/full-suite-verification.r1.md`
  reports the isolated new-class run as `Passed! - Failed: 0, Passed: 4,
  Skipped: 0, Total: 4`, plus a clean `dotnet format --verify-no-changes` and
  `dotnet build`, plus a full-suite `dotnet test` run (6621 passed / 4 skipped
  / 105 failed) where every failure is independently confirmed to be the
  pre-existing, environment-only `KnowledgeBaseRepositoryIntegrationTests`
  Docker/Testcontainers dependency — none of the three Invoices-related test
  classes appear in that failure list.
- Independently re-read the diff hunk for the new test file directly from
  `git diff` (not just the working-tree file) to confirm the file as
  committed matches what was reviewed — no drift between what the per-task
  reviewers approved and what landed in the branch.

## Conclusion

No correctness bugs found. The four new tests are a precise, non-overlapping
mapping onto the spec's four functional requirements (FR-1 through FR-4),
each pinning exact behavior (not just "no exception thrown") against the
real handler code, with no production code touched. No advisory
cleanups worth flagging — the file is small, has no duplicated logic to
extract, and follows the established sibling-test-file pattern exactly.
