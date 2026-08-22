# Implementation: scaffold-invoice-source-test-file

## What was implemented
Created the new unit test file `ShoptetApiInvoiceSourceTests.cs` for `ShoptetApiInvoiceSource.GetAllAsync`, with its shared `Build*` helper methods (`BuildMapper`, `BuildSource`, `BuildDto`) and the first test scenario covering FR-1: single-invoice-fetch mode, invoice found. The file content matches exactly what the task-context spec prescribed (verbatim), using `Moq` to mock `IShoptetInvoiceClient` and the real `ShoptetInvoiceMapper` (with real `BillingMethodMapper` / `ShippingMethodMapper` collaborators) so the mapping logic runs unmocked.

This is a test-only, coverage-only change. No production code in `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi` was modified.

## Files created/modified
- `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetApiInvoiceSourceTests.cs` — new file containing the `ShoptetApiInvoiceSourceTests` test class with:
  - `BuildMapper()` — constructs a real `ShoptetInvoiceMapper` with real `BillingMethodMapper` and `ShippingMethodMapper` (via `Options.Create(new ShoptetApiSettings())`)
  - `BuildSource(Mock<IShoptetInvoiceClient>)` — constructs the `ShoptetApiInvoiceSource` under test with a mocked client, the real mapper, and a no-op logger
  - `BuildDto(...)` — builds a minimal `ShoptetInvoiceDto` fixture
  - `GetAllAsync_SingleInvoiceModeFound_ReturnsSingleBatchWithMappedInvoice` — the FR-1 test: given `IssuedInvoiceSourceQuery.InvoiceId` set (single-invoice/`QueryByInvoice` mode), asserts the client's `GetInvoiceAsync` is called once with the requested code, `ListInvoicesAsync` is never called, exactly one batch is returned with `BatchId == RequestId`, and the mapped invoice's `OrderCode` equals the source DTO's `Code` (proving the real mapper ran, since `ShoptetInvoiceMapper.Map` swaps `Code`/`OrderCode`).

## Tests
- `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetApiInvoiceSourceTests.cs::GetAllAsync_SingleInvoiceModeFound_ReturnsSingleBatchWithMappedInvoice` — new xUnit fact, covers the single-invoice-fetch-found branch of `ShoptetApiInvoiceSource.GetAllAsync`.

## How to verify
```bash
dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj -c Release --filter "FullyQualifiedName~ShoptetApiInvoiceSourceTests.GetAllAsync_SingleInvoiceModeFound_ReturnsSingleBatchWithMappedInvoice"
```
Result: `Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1`.

`dotnet format Anela.Heblo.sln --include backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetApiInvoiceSourceTests.cs --verify-no-changes` reports no diagnostics (clean).

## Notes
- **Build environment gotcha (not code-related):** `dotnet test`/`dotnet build` in Debug configuration in this sandbox intermittently stalls indefinitely inside the API project's `GenerateAccessMatrix` pre-build target (`Anela.Heblo.API.csproj`, `BeforeTargets="Build"`, condition `Configuration == Debug`), which shells out via `Exec` to `dotnet run --project ../../tools/Anela.Heblo.AccessMatrixGen`. The nested `dotnet run` appears to leave the outer MSBuild worker nodes deadlocked after it completes, with zero CPU progress for many minutes. Building/testing with `-c Release` (which skips this Debug-only target entirely) reliably completes in well under a minute and was used to verify this task. No production or build-config files were changed to work around this — it's purely how the test/verification command was invoked. Worth a `memory/gotchas/` note for future sessions in this repo/sandbox.
- Confirmed via `git status` that the `GenerateAccessMatrix` target's generated files (`Feature.generated.cs`, `AccessMatrix.generated.cs`, `AccessRoles.generated.cs`, `accessMatrix.generated.ts`, `access-matrix-entra.generated.json`) were not left modified in the working tree after the Debug-config build attempts — only the intended test file is new/staged.
- No `add-single-fetch-null-test`, `add-currency-filter-*`, or `add-null-detail-guard-test` scenarios were added — those are separate, later tasks in the pipeline (`state.json` lists them as `pending`), out of scope for this scaffold task.

## PR Summary
Added the first unit test file for `ShoptetApiInvoiceSource`, closing part of a coverage gap (18.4% line coverage vs a 60% threshold) that was previously only exercised by a live-credential integration test. This task adds the test file scaffold plus one scenario (FR-1: single-invoice fetch, invoice found), mocking `IShoptetInvoiceClient` while running the real `ShoptetInvoiceMapper` so mapping behavior is verified end-to-end for this path. Test-only change; no production code touched.

### Changes
- `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetApiInvoiceSourceTests.cs` — new test class with `Build*` helpers and the FR-1 single-invoice-fetch-found test

## Status
DONE
