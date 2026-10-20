# Code Review: extract-sortby-helper-and-refactor-applysorting

## Summary
The implementation replaces `ApplySorting`'s repeated `sortDescending ? OrderByDescending : OrderBy` ternary with a single `SortBy<TKey>` helper, exactly matching the task context's specified code block. Every selector lambda in the rewritten method matches the pre-refactor arm it replaces, and all verification steps (build, targeted test suite, format check) confirm no behavior change.

## Review Result: PASS

### task: extract-sortby-helper-and-refactor-applysorting
**Status:** PASS

## Docs to Update
(none — internal, private-method refactor with no public API, contract, or documented-behavior change)

## Overall Notes
- Diffed every arm of the new `switch` against the pre-refactor code (as captured in the implementation artifact and confirmed directly against the file): all 13 named arms, the default arm, and the pre-switch null/whitespace guard use identical selector lambdas to before, now routed through `SortBy`.
- `GetProductMarginsHandlerTests` — 7/7 passed, including the two characterization tests (`Handle_SortByM0AmountDescending_OrdersByCalculatedM0Amount`, `Handle_SortByM0PercentageAscending_OrdersByCalculatedM0Percentage`) added specifically to lock in behavior ahead of this refactor.
- `dotnet build Anela.Heblo.sln` — 0 errors, no new warnings.
- `dotnet format --verify-no-changes` — no formatting issues in the touched file; pre-existing unrelated issues in `MarketingPerformance` test files (from PR #4235) correctly left untouched, consistent with the surgical-changes rule.
- Full-solution `dotnet test` shows 111 failures, all in Docker/Testcontainers-dependent integration tests (Postgres containers, Flexi integration fixtures) unrelated to Catalog/GetProductMargins — expected in a sandboxed environment without a Docker daemon, not caused by this change.
