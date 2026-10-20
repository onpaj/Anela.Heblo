# Implementation Plan: ShoptetApiInvoiceSource coverage gap

### task: verify-existing-tests
Run `dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests --filter "FullyQualifiedName~ShoptetApiInvoiceSourceTests"` and confirm all tests pass, covering scenarios (a) to (d) from issue #3960 (see `Unit/ShoptetApiInvoiceSourceTests.cs`). Then run with `--collect:"XPlat Code Coverage"` and record line coverage for `ShoptetApiInvoiceSource`. Check `.github/workflows/ci-*.yml` to see whether this test project is included in the coverage run that produced the 18.4% figure; report findings in the PR description.

### task: add-residual-tests
Only for lines uncovered per coverage output, add tests to `Unit/ShoptetApiInvoiceSourceTests.cs` following existing helpers: (1) list item with `Price = null` is filtered out; (2) empty list returns one batch with `BatchId == RequestId`, empty invoices, and no `GetInvoiceAsync` calls. Skip this task if coverage is already at or above 60% and no relevant branch is uncovered (still acceptable to add these two small tests). Run `dotnet build` and `dotnet format` on the test project.
