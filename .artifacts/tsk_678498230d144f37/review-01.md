# Review: Server-side validation for GenerateArticleRequest.Scope and Length

## Verdict

**Approve.** The implementation (commit `268543c0`) matches `plan-01.md`, `design-01.md`, and `architecture-01.md` exactly, with no deviations. Build, format, and tests all pass.

## What was implemented

- `GenerateArticleRequest.cs`: added `[Required]` + `[AllowedValues("overview", "deep-dive", "how-to", "comparison", ErrorMessage = ...)]` to `Scope`, and `[Required]` + `[AllowedValues("brief (500w)", "medium (1000w)", "long (2000w)", ErrorMessage = ...)]` to `Length` — verbatim as specified in the finding and design.
- New `backend/test/Anela.Heblo.Tests/Controllers/ArticlesControllerTests.cs`, following the exact directive from `architecture-01.md` (the `HebloWebApplicationFactory` + `[Collection("WebApp")]` integration-test pattern, since `GenerateArticleHandlerTests.cs` bypasses model binding and can't exercise this fix). Covers:
  - All 4 allowed `Scope` values → 200.
  - All 3 allowed `Length` values → 200.
  - Invalid `Scope` → 400, error body mentions `Scope`, no `Article` persisted (verified via article count before/after).
  - Invalid `Length` → 400, error body mentions `Length`, no `Article` persisted.
  - Omitted `Scope`/`Length` → 200, defaults applied, matching the plan's flagged open question (now resolved as a passing test rather than an assumption).
- `GenerateArticleHandler.cs` and `WriteArticleStep.cs` are untouched, as scoped. No frontend, domain, or persistence changes, as scoped.

## Verification performed

- `dotnet build Anela.Heblo.sln` — **Build succeeded, 0 errors** (one pre-existing, unrelated warning from the access-matrix codegen tool caused by concurrent worktree builds sharing `access-matrix.generated.json`; not touched by this change and not a regression).
- `dotnet test --filter "FullyQualifiedName~ArticlesControllerTests"` — **10/10 passed**.
- `dotnet test --filter "FullyQualifiedName~GenerateArticleHandlerTests"` — **5/5 passed** (no regression in the existing handler-level tests, confirming they remain valid/unmodified).
- `dotnet format Anela.Heblo.sln --verify-no-changes` on both changed files — clean, no formatting violations.
- Confirmed `[Collection("WebApp")]` + `IClassFixture<HebloWebApplicationFactory>` matches the established pattern used by `MarketingCalendarControllerTests`/`PurchaseOrdersControllerTests`.
- Confirmed the `ArticlesController.Generate` endpoint is `[FeatureAuthorize(Feature.Marketing_Article, AccessLevel.Write)]`-protected, and that `HebloWebApplicationFactory` loads `appsettings.Test.json` with mock authentication in the `Test` environment — so the new tests authenticate correctly without extra setup, same as other protected-controller tests.
- Confirmed `GenerateArticleHandler` only enqueues a Hangfire job (`GenerateArticleJob`) and that the test factory removes the Hangfire background server — so `Generate_WithAllowedScope_ReturnsOk`/`Generate_WithAllowedLength_ReturnsOk` never actually invoke the LLM pipeline; they only assert the request reaches the handler and gets queued. This makes the tests fast and deterministic, consistent with the architecture doc's intent.
- Confirmed `Article.DefaultScope`/`DefaultLength` values are members of the new allow-lists, so the "omitted fields still work" test case is exercising real, not vacuous, behavior.
- Diffed against `GenerateArticleHandlerTests.cs` history — confirmed untouched, as the plan/architecture required.

## Notes (non-blocking)

- The architecture doc flagged two informational items as "not blocking, note the outcome": (1) whether Swashbuckle renders `[AllowedValues]` as an OpenAPI `enum`, and (2) a one-off DB check for pre-existing out-of-vocabulary rows. Neither is evidenced in the implementation diff or test file, and neither was required for approval — both were explicitly scoped as informational/non-blocking in `architecture-01.md`, so their absence doesn't block this review.
