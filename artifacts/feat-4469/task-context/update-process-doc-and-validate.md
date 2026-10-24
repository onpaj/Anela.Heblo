### task: update-process-doc-and-validate
**Files:** modify `docs/processes/calc-purchase-stock-analysis.md`.

- [ ] Add the four new Contracts files (`StockAnalysisItemDto.cs`, `StockAnalysisSummaryDto.cs`, `LastPurchaseInfoDto.cs`, `StockAnalysisFilter.cs`) to the `owns:` list; bump `verified_at` to today. Update any prose naming the request as the filter input (the doc references the handler's filter step).
- [ ] Run `python3 scripts/process-docs/check.py index`; resolve any complaint.
- [ ] Run `dotnet build`, `dotnet format backend/Anela.Heblo.sln --verify-no-changes` (apply formatting if needed), and the Purchase test filter again.
- [ ] Confirm the generated OpenAPI TS client shows no diff for the three DTO schemas (`git status` after build).
- [ ] Commit `docs(purchase): update stock analysis process doc for new Contracts`.
