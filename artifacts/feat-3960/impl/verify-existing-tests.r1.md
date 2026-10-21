# verify-existing-tests r1

No source changes (verification-only task).

## Results
- `dotnet test backend/test/Anela.Heblo.Adapters.Shoptet.Tests -c Release --filter "FullyQualifiedName~ShoptetApiInvoiceSourceTests"`: Passed 6, Failed 0 (Total 6).
- Tests cover issue #3960 scenarios (a) single invoice found, (b) single invoice null/not found, (c) mixed-currency list filter (+ case-insensitive theory), (d) null detail skipped without aborting batch.
- Coverage (XPlat cobertura): `ShoptetApiInvoiceSource` line-rate 0.80 (class) and 1.0 for the async state machine / lambdas. Uncovered lines: 79 and 82 only (`CommitAsync` / `FailAsync` no-op methods).
- Unclear branch not covered: list item with `Price == null` (the `i.Price?.CurrencyCode` null path) and empty list result -> addressed by next task add-residual-tests.

## CI coverage inclusion
- `.github/workflows/ci-main-branch.yml` and `ci-feature-branch.yml` run `dotnet test Anela.Heblo.sln --collect:"XPlat Code Coverage" --filter "Category!=Playwright&Category!=Integration"`; the Shoptet.Tests project is in the sln (and is explicitly built) and the tests carry no excluded Category, so they ARE included.
- The test file was added in commit 925de5ca (2026-10-08), after the issue's CI run (2026-08-24, 18.4%); the 18.4% figure predates these tests, so the gap is already closed (80% now).

## Environment note
Debug build of Anela.Heblo.API runs an MSBuild access-matrix Exec that hung in this sandbox; Release configuration (skips that target) was used for the test run.
