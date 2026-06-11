Plan saved to `docs/superpowers/plans/2026-06-03-remove-unused-ilotsclient-getcatalogdetail.md`.

**Summary:** 4-task plan for the `ILotsClient` removal refactor.

- **Task 1** edits the handler (using, field, ctor parameter, ctor assignment).
- **Tasks 2 & 3** mirror the change in `GetCatalogDetailHandlerTests` and `GetCatalogDetailHandlerFullHistoryTests` (using, mock field, instantiation, ctor argument).
- **Task 4** is the verification gate: regression-guard greps (confirming `ILotsClient` still lives in the domain interface, DI registration, `CatalogDataRefreshService`, and `CatalogRepository`), full `dotnet build`, targeted `dotnet test --filter` for both handler fixtures, broader Catalog-test safety net, scoped `dotnet format`, diff inspection, and a single atomic commit.

Each code edit shows the exact before/after; each command has explicit expected output. The plan deliberately leaves the solution in a non-compiling intermediate state between Tasks 1–3 and resolves at Task 4 — appropriate for a signature-coupled refactor where TDD doesn't naturally apply (existing tests already pin the behavior).

Self-review confirms all spec FRs (FR-1 through FR-4), both arch-review amendments, and the surgical scope ("don't touch `CatalogRepositoryTests` / `CatalogRepositoryCacheOptimizationTests` / DI registration") are explicitly covered.