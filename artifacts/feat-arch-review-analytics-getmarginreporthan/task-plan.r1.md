Plan saved to `docs/superpowers/plans/2026-06-02-analytics-validation-pipeline.md`.

**Summary of the plan (7 tasks, ~30 steps, TDD-ordered):**

1. **Task 1** — Build `ValidationResultBehavior<TRequest, TResponse>` (a typed MediatR pipeline behavior that returns a `BaseResponse` envelope instead of throwing), driven by 6 unit tests.
2. **Tasks 2 & 3** — Annotate both validators with `WithErrorCode(((int)ErrorCodes.X).ToString())` and `WithState(...)` so failures carry the exact `ErrorCode` + `Params` shape (`startDate`/`endDate`/`period`/`field`) the frontend depends on; add validator unit tests using `FluentValidation.TestHelper`.
3. **Task 4** — Wire the behavior per-request in `AnalyticsModule` (matching the established `CatalogModule`/`PhotobankModule` pattern) and verify with a real `IMediator`-based integration test that fails before wiring and passes after.
4. **Tasks 5 & 6** — Only **after** the pipeline is proven to enforce validation, delete the duplicate `if`-blocks from both handlers and prune the now-redundant handler unit tests (3 tests in `GetMarginReportHandlerTests`, 2 in `GetProductMarginAnalysisHandlerTests`).
5. **Task 7** — Run the project's full validation gates (`dotnet build -warnaserror`, `dotnet format --verify-no-changes`, `dotnet test`).

The plan internalizes the arch review's critical correction (validators were not in fact wired into the pipeline today, so the in-handler guards are load-bearing — not dead code — until Task 4 lands), and addresses every arch-review amendment as a discrete step or task.