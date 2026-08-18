# Implementation: final-validation

## What was implemented
No code changes. Ran the project-standard validation gate (`dotnet build`, `dotnet format --verify-no-changes`) plus the full `Anela.Heblo.Adapters.Shoptet.Tests` test project to confirm `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetApiInvoiceSourceTests.cs` (added by earlier tasks in this feature) compiles cleanly, is correctly formatted, and introduces no regressions.

## Files created/modified
- none — this was a validation-only pass; no source files were changed. (The only working-tree diff observed, `artifacts/feat-3939/state.json`, is pipeline bookkeeping unrelated to this task and was left untouched/uncommitted.)

## Tests

**Step 1 — `dotnet build Anela.Heblo.sln`**
Build succeeded. `0 Error(s)`, 252 pre-existing warnings (all in unrelated files, e.g. `Anela.Heblo.Tests`), none introduced by the new test file.

**Step 2 — `dotnet format Anela.Heblo.sln --verify-no-changes`**
Exit code 0, no output, no files listed as needing changes, no working-tree diff produced. `ShoptetApiInvoiceSourceTests.cs` is already compliant with repo formatting rules — no format fix or commit needed (Step 4 skipped as instructed).

**Step 3 — `dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj`**
Full project run: `Failed: 13, Passed: 119, Skipped: 1, Total: 133`.

- The 6 new tests in `Unit/ShoptetApiInvoiceSourceTests.cs` (verified via `--filter "FullyQualifiedName~Unit.ShoptetApiInvoiceSourceTests"`) all **Passed** — covering FR-1 through FR-5, with FR-4 (`GetAllAsync_ListModeCurrencyFilter_IsCaseInsensitive`) contributing the two expected `InlineData` cases:
  - `GetAllAsync_SingleInvoiceModeFound_ReturnsSingleBatchWithMappedInvoice`
  - `GetAllAsync_SingleInvoiceModeNotFound_ReturnsBatchWithEmptyInvoiceList`
  - `GetAllAsync_ListModeCurrencyFilter_ExcludesNonMatchingCurrency`
  - `GetAllAsync_ListModeCurrencyFilter_IsCaseInsensitive("CZK","czk")`
  - `GetAllAsync_ListModeCurrencyFilter_IsCaseInsensitive("czk","CZK")`
  - `GetAllAsync_ListModeNullDetail_ExcludesAffectedCodeWithoutAbortingBatch`
- Re-running with `--filter "FullyQualifiedName!~Integration"` (all `Unit/` + `Expedition/` tests) confirmed **105/105 passed, 0 failed** — no regression anywhere outside `Integration/`.
- All 13 failures are in `Integration/` (`ShoptetApiInvoiceSourceIntegrationTests`, `ShoptetStockClientIntegrationTests`, `ShoptetTestEnvironmentHydrationTests`, `PickingListIntegrationTests`), each failing because this sandbox has no `Shoptet:ApiToken`, `Shoptet:StatusId:EXP`, or stock-URL configuration/secrets available — a pre-existing environment condition confirmed unrelated to this feature branch: `git diff --stat origin/main...HEAD` shows the branch touches only the new `Unit/ShoptetApiInvoiceSourceTests.cs` file and `artifacts/` bookkeeping — zero lines changed under `Integration/`. Note: the task spec expected these to "stay inert/skipped" without credentials; in reality most of them throw during fixture construction/HTTP call rather than skip cleanly (only `CaptureInvoice126000039Fixture.Capture` actually reports `Skipped`) — this is pre-existing integration-test design, not a regression from the new file, and is out of this task's scope to fix.

## How to verify
```bash
cd /home/user/worktrees/feature-3939-Coverage-Gap-Adapters-Shoptetapiinvoicesource-Quer
dotnet build Anela.Heblo.sln
dotnet format Anela.Heblo.sln --verify-no-changes
dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj --filter "FullyQualifiedName!~Integration"
```
Expect: build succeeds with 0 errors, format reports no changes, and the filtered test run shows `Failed: 0, Passed: 105`.

## Notes
- No commit was needed: `dotnet format` made zero changes, and no source files required edits.
- The 13 pre-existing `Integration/` failures (missing `Shoptet:ApiToken` / `Shoptet:StatusId:EXP` / stock URL secrets) are an expected sandbox limitation — no live Shoptet credentials are configured here, and per `CLAUDE.md` this is normal ("No sandbox — every call hits a live store"). They are unrelated to the coverage-gap work in this feature and were left untouched, consistent with this task's validation-only scope.
- `artifacts/feat-3939/state.json` shows as modified in `git status` (pipeline-managed bookkeeping, not a file this task edited) and was left as-is/uncommitted.

## PR Summary
Validation-only pass for the Shoptet invoice-source coverage-gap work: `dotnet build` succeeds with 0 errors, `dotnet format --verify-no-changes` reports the new test file is already compliant (no fix/commit needed), and the full `Anela.Heblo.Adapters.Shoptet.Tests` suite runs with all 6 new `ShoptetApiInvoiceSourceTests` cases (FR-1–FR-5) passing and all 105 `Unit/`+`Expedition/` tests passing with zero regressions. The only failures (13, all under `Integration/`) are pre-existing and environmental — they require live `Shoptet:ApiToken`/`Shoptet:StatusId:EXP`/stock-URL credentials not present in this sandbox, and `git diff --stat` against `origin/main` confirms this branch never touched any file under `Integration/`.

## Status
DONE
