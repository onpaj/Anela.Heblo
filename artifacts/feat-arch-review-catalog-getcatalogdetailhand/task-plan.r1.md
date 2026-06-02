Plan saved to `docs/superpowers/plans/2026-06-02-extract-compute-from-date-helper.md`.

## Summary

The plan decomposes the refactor into six tasks with TDD discipline throughout:

- **Task 0** — R1 data-invariant gating step (verify no pre-2020 records in `CatalogPurchaseRecord` / `CatalogManufactureRecord` before adopting `2020-01-01` as the floor).
- **Task 1** — Establish green baseline (`dotnet build` + Catalog tests + `dotnet format --verify-no-changes`).
- **Task 2** — Add `HISTORY_FLOOR_DATE` to `CatalogConstants` (RED: update `ContainsOnlyExpectedMembers` + two new constant tests; GREEN: add the constant).
- **Task 3** — Add `ComputeFromDate` helper and refactor Pattern A handlers (`GetManufactureCostHistoryFromMargins`, `GetMarginHistoryFromMargins`) with existing tests as the safety net.
- **Task 4** — Refactor Pattern B handlers (`GetPurchaseHistoryFromAggregate`, `GetManufactureHistoryFromAggregate`) driven by a new RED test that places a `2019-12-31` record in the fixture and asserts it is excluded under the unified filter (arch-review amendment 2).
- **Task 5** — Repo-wide audit confirming `2020-01-01` literal is eliminated from Catalog source.
- **Task 6** — Final validation gate per `CLAUDE.md` (`dotnet build` + `dotnet format` + full Catalog + full backend test runs + diff scope check).

Every step contains the exact replacement code, exact commands, and expected outcomes. The plan honors the spec's `Skip Design: true` decision and respects the explicit out-of-scope handlers (`GetSalesHistoryFromAggregate`, `GetConsumedHistoryFromAggregate`).