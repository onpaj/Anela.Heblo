# Implementation: update-process-doc-and-validate (r1)

- `docs/processes/calc-purchase-stock-analysis.md`: added StockAnalysisItemDto, StockAnalysisSummaryDto, LastPurchaseInfoDto, StockAnalysisFilter to `owns:`; bumped `verified_at`; noted filter inputs now flow via `StockAnalysisFilter`.
- `python3 scripts/process-docs/check.py index`: OK, no INDEX diff.
- `dotnet build Anela.Heblo.sln`: 0 errors. `dotnet format --verify-no-changes`: clean.
- Purchase tests: 436 passed, 1 failed (PostgresSharedContainerFixture - Docker/Testcontainers unavailable in this sandbox; environmental, unrelated).
- OpenAPI TS client: no diff after build (git status clean).
