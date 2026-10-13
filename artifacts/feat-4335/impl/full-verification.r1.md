# Implementation: full-verification

## What was implemented
This is a verification-only task (task-context lists "Files: None") — no source changes were
made. Ran the 7-step verification checklist for feat-4335 (moving the Azure Blob print-sink
config out of the Application-layer `PrintPickingListOptions` into the new Adapters.Azure
`AzureBlobPrintSinkOptions`), confirming task 1 (`extract-azure-blob-print-sink-options`) fully
and cleanly satisfies the spec.

## Files created/modified
None. Working tree is clean at the end of this task (see Notes for two changes that were made
temporarily to unblock verification and then reverted before finishing, per the surgical-changes
rule — neither belongs to this task's scope).

## Steps executed & results

1. **Grep — removed properties**: `git grep -n 'PrintPickingListOptions' backend/ | grep -E
   '\.PrintSink\b|\.BlobConnectionString\b|\.BlobContainerName\b'` returns one line, not the
   expected empty output — but it's a comment in
   `AzureBlobPrintSinkOptionsBindingTests.cs` ("`// Assert — default must match the pre-refactor
   PrintPickingListOptions.BlobContainerName default...`"), explanatory prose, not a code
   reference. `PrintPickingListOptions.cs` itself declares none of the three removed properties.
   Verified clean.

2. **Grep — PrintSink dispatch untouched**: `git grep -n 'configuration\["ExpeditionList:PrintSink"\]'
   backend/` returns two lines, not the expected exactly-one — the second is a doc-comment in
   `PrintPickingListOptions.cs`'s class-summary (added by task 1) that explains where `PrintSink`
   dispatch now lives, not a second dispatch call site. The real, single call site in
   `ServiceCollectionExtensions.cs:437` is unchanged from before this plan. Verified clean.

3. **Grep — no appsettings changes**: `git diff --stat HEAD -- 'backend/src/Anela.Heblo.API/appsettings*.json'`
   is empty. Confirmed.

4. **`dotnet format`**: the task-context's literal path (`backend/Anela.Heblo.sln`) does not
   exist — the solution file is at the repo root (`Anela.Heblo.sln`), consistent with
   `docs/development/setup.md`. Ran `dotnet format Anela.Heblo.sln` from the repo root instead.
   It reformatted two files outside this plan's scope
   (`GetMarketingPerformanceComparisonHandlerTests.cs`,
   `GetMarketingPerformanceMonthsHandlerTests.cs` — pre-existing whitespace drift in an unrelated
   module) with no changes to any file this plan touches. Reverted those two files
   (`git checkout --`) per the "surgical changes, don't touch adjacent formatting" project rule —
   they are not part of feat-4335.

5. **`dotnet build` (as-is, unpatched)**: **FAILS** — but with exactly the one pre-existing,
   already-documented, unrelated error: `CS1503` in
   `RecurringJobSeeder.cs:51` (`HasSeededFieldsChanged(existing, config)` — `existing` is a
   `List<RecurringJobConfiguration>`, should be `existingConfig`). This was already found and
   flagged by both the developer and reviewer of task 1
   (`impl/extract-azure-blob-print-sink-options.r1.md`,
   `review/extract-azure-blob-print-sink-options.r1.md`); confirmed again here that it is on
   `origin/main` (commit `882659fe6`, PR #4324), i.e. pre-dates and is unrelated to feat-4335.
   No warning or error anywhere touches `ExpeditionList`, `AzureAdapterModule`,
   `AzureBlobPrintSinkOptions`, or `PrintPickingListOptions`.

6. **`dotnet test` (full suite, as-is)**: cannot run — the test project transitively references
   `Anela.Heblo.Application`, which fails to build for the same pre-existing reason as Step 5.
   To actually validate the feature (rather than stop at "blocked"), used the same technique
   task 1's developer used: applied a one-line **local, temporary** fix to
   `RecurringJobSeeder.cs` (`existing` → `existingConfig`), then:
   - Full `dotnet build`: **0 errors**, 161 warnings (all pre-existing, none in this feature's
     files).
   - Full `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`:
     **7879 passed / 111 failed / 4 skipped / 7994 total**. All 111 failures are
     `System.ArgumentException: Docker is either not running or misconfigured` from
     Testcontainers-backed integration tests (e.g. `KnowledgeBaseRepositoryIntegrationTests`)
     that require a Docker daemon unavailable in this sandbox — confirmed **zero** of the 111
     failures are in `ExpeditionList`/`PrintQueueSink`/`AzureBlob` (grep of the failure list).
   - Scoped rerun `--filter "FullyQualifiedName~ExpeditionList|FullyQualifiedName~PrintQueueSink"`:
     **193/193 passed** — same count task 1's reviewer already confirmed.
   - Reverted the local one-line fix (`git checkout --`) so it does not appear in this task's
     diff — fixing it is out of this task's scope ("Files: None") and outside feat-4335 entirely.

7. **Commit formatting changes**: nothing to commit — the only `dotnet format` output was the two
   unrelated files reverted in Step 4.

## Tests
No new tests (verification-only task). Confirmed via the scoped rerun that all 193 existing
`ExpeditionList`/`PrintQueueSink` tests (including `AzureBlobPrintSinkOptionsBindingTests` and
`CombinedPrintQueueSinkRegistrationTests` from task 1) still pass, and confirmed via the full
7994-test run (with the pre-existing blocker locally/temporarily patched) that none of the 111
failing tests touch this feature's code — all 111 are pre-existing Docker/Testcontainers
environment failures.

## How to verify
```bash
git grep -n 'PrintPickingListOptions' backend/ | grep -E '\.PrintSink\b|\.BlobConnectionString\b|\.BlobContainerName\b'
git grep -n 'configuration\["ExpeditionList:PrintSink"\]' backend/
git diff --stat HEAD -- 'backend/src/Anela.Heblo.API/appsettings*.json'
dotnet build   # fails with 1 pre-existing, unrelated CS1503 in RecurringJobSeeder.cs — see Notes
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ExpeditionList|FullyQualifiedName~PrintQueueSink"   # 193/193 pass
```

## Notes

**Blocking, pre-existing, unrelated issue (not fixed here, out of scope):**
`backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs:51`
has a `CS1503` compile error (`HasSeededFieldsChanged(existing, config)` should be
`HasSeededFieldsChanged(existingConfig, config)` — introduced by PR #4324, already on
`origin/main`, unrelated to `ExpeditionList`). This currently blocks a plain `dotnet build`/
`dotnet test` of the whole solution on any branch based on current `main`, not just this one.
It was already flagged by task 1's developer and reviewer; flagging again since it's the reason
this task's Step 5/6 cannot report a literally clean whole-solution build/test without a local,
reverted workaround. **This needs a human's attention or a separate one-line-fix issue** — it is
not something feat-4335 should fix (different module, no relation to `ExpeditionList`).

**Two grep steps had one extra, benign match each** (a comment and a doc-comment respectively,
not code) — see Steps 1 and 2 above. Neither indicates any remaining reference to the removed
properties or a second `PrintSink` dispatch site.

**`dotnet format`'s task-context command path was wrong** (`backend/Anela.Heblo.sln` doesn't
exist; solution is at repo root) — used the correct path instead, consistent with
`docs/development/setup.md`.

All of FR-1 through FR-5 and NFR-1 through NFR-3 from `spec.r1.md` are confirmed satisfied.

## PR Summary
Verification pass for feat-4335: confirmed the `PrintPickingListOptions` / `AzureBlobPrintSinkOptions`
split (done in task 1) fully satisfies the spec — no remaining code references to the three
removed properties, `PrintSink` dispatch untouched, no `appsettings*.json` changes, and all 193
`ExpeditionList`/`PrintQueueSink` tests green. No source changes in this task. Surfaced one
pre-existing, unrelated compile error (`RecurringJobSeeder.cs`, from PR #4324, already on `main`)
that currently blocks a literal whole-solution `dotnet build`/`dotnet test` — confirmed via a
local, reverted one-line patch that the feature itself is unaffected (full suite: 7879 passed,
111 failed — all 111 pre-existing Docker/Testcontainers environment failures, 0 in this
feature's code).

### Changes
- None (verification only)

## Status
DONE_WITH_CONCERNS
