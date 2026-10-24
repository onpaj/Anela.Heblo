### task: verify-existing-tests
Run `dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests --filter "FullyQualifiedName~ShoptetApiInvoiceSourceTests"` and confirm all tests pass, covering scenarios (a) to (d) from issue #3960 (see `Unit/ShoptetApiInvoiceSourceTests.cs`). Then run with `--collect:"XPlat Code Coverage"` and record line coverage for `ShoptetApiInvoiceSource`. Check `.github/workflows/ci-*.yml` to see whether this test project is included in the coverage run that produced the 18.4% figure; report findings in the PR description.

