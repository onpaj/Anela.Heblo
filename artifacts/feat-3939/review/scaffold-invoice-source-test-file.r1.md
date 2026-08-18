# Code Review: scaffold-invoice-source-test-file

## Summary
The implementation creates exactly the test file specified in the task context, byte-for-byte matching the prescribed content, and the single FR-1 scenario (single-invoice fetch, invoice found) passes cleanly. This is a test-only, coverage-only addition — no production code was touched, consistent with the spec's framing of this as a coverage gap fix rather than a behavior change.

## Review Result: PASS

### task: scaffold-invoice-source-test-file
**Status:** PASS

Verification performed directly against the worktree:
- Diffed the committed file (`git show HEAD`) against the exact content block in `task-context/scaffold-invoice-source-test-file.md` — identical, including the explanatory comment about `Code`/`OrderCode` swapping.
- Confirmed the file is at the exact specified path: `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetApiInvoiceSourceTests.cs`.
- Ran `dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Anela.Heblo.Adapters.Shoptet.Tests.csproj -c Release --filter "FullyQualifiedName~ShoptetApiInvoiceSourceTests.GetAllAsync_SingleInvoiceModeFound_ReturnsSingleBatchWithMappedInvoice"` — result: `Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1`, matching the spec's required expectation exactly.
- Cross-checked the production types the test depends on (`ShoptetApiInvoiceSource`, `ShoptetInvoiceMapper`, `IssuedInvoiceSourceQuery`, `IShoptetInvoiceClient`, `ShoptetApiSettings`) against their actual signatures in `backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi` — the mock setup, constructor arguments, and assertions all align with the real implementation (in particular, `ShoptetInvoiceMapper.Map` really does set `mapped.OrderCode = src.Code`, so asserting `batch.Invoices[0].OrderCode == "INV-1"` is a correct and meaningful check that the real mapper ran on the mocked DTO).
- Confirmed `git status` shows only the new test file staged, plus the pipeline's own `state.json` checkpoint update — no production code or unrelated generated files were modified.
- The change is committed on the feature branch (`test: add ShoptetApiInvoiceSource single-invoice-fetch-found coverage (FR-1)`).

No functional requirement gaps, no architecture deviations, no missing tests relative to what this specific task asked for (later tasks in the pipeline add the remaining scenarios).

## Docs to Update
(none — internal test-only addition, no public behavior, CLI, or documented pattern changed)

## Overall Notes
The developer's implementation notes flag a sandbox-specific build annoyance: `dotnet test`/`dotnet build` in Debug configuration intermittently stalls in the API project's `GenerateAccessMatrix` pre-build target, and Release configuration was used instead to get a reliable, fast verification run. This is an environment/tooling observation, not a code defect — it does not affect correctness of the test or the production code, and no build configuration files were changed to work around it.

**Status:** PASS
