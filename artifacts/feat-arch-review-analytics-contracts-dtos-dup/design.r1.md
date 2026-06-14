`design.md` written to `artifacts/feat-arch-review-analytics-contracts-dtos-dup/design.md`.

Since the arch-review confirmed no UI and no design ambiguities, the document is intentionally lean — it covers the two backend-only sections:

- **Component Design** — the four files being modified (two `Response.cs`, two `Handler.cs`) with the exact change per file, plus explicit no-change callouts for `IReportBuilderService`, the three `Contracts/` DTOs, and the frontend (only the generated client renames).
- **Data Schemas** — the canonical field definitions for all three unified DTOs (with the explicit note that `MonthlyMarginBreakdownDto` has no `MarginPercentage`), the two updated response class shapes, and a wire-shape note confirming JSON is byte-equivalent.