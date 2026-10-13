# Implementation: feedback-list-boundary-validation

## What was implemented
Moved paging/sort validation for the `GetArticleFeedbackList` use case from the handler
to the API boundary, using the same `FluentValidation` + `ValidationResultBehavior<,>`
MediatR pipeline pattern applied to `ListArticles` in the previous task. The controller
action now binds `GetArticleFeedbackListRequest` directly via `[FromQuery]` instead of
manually constructing it from individual parameters, so a
`GetArticleFeedbackListRequestValidator` runs automatically in the MediatR pipeline
before `GetArticleFeedbackListHandler.Handle` executes. The handler no longer clamps
`Page`, falls back `PageSize` to 20, or falls back `SortBy` to `"CreatedAt"` — it trusts
the validated request.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/GetFeedbackList/GetArticleFeedbackListRequestValidator.cs` — new `FluentValidation` validator: `Page >= 1`, `PageSize` in `{10, 20, 50}`, `SortBy` in `{"CreatedAt", "PrecisionScore", "StyleScore"}`.
- `backend/src/Anela.Heblo.Application/Features/Article/ArticleModule.cs` — registers `IValidator<GetArticleFeedbackListRequest>` and the `ValidationResultBehavior<GetArticleFeedbackListRequest, GetArticleFeedbackListResponse>` pipeline behavior, alongside the existing `ListArticlesRequest` registrations.
- `backend/src/Anela.Heblo.API/Controllers/ArticlesController.cs` — `FeedbackList` action now binds `[FromQuery] GetArticleFeedbackListRequest request` directly instead of manually constructing the request from `hasFeedback`/`requestedBy`/`sortBy`/`sortDescending`/`page`/`pageSize` parameters.
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/GetFeedbackList/GetArticleFeedbackListHandler.cs` — removed the `AllowedPageSizes`/`AllowedSortColumns` static arrays and the three compensating clamp/fallback lines; the handler now passes `request.Page`/`request.PageSize`/`request.SortBy` straight through.
- `backend/test/Anela.Heblo.Tests/Article/UseCases/GetArticleFeedbackListHandlerTests.cs` — removed the two allowlist-fallback tests (`Handle_UnknownSortBy_FallsBackToCreatedAt`, `Handle_PageSizeOutsideAllowlist_FallsBackTo20`) whose asserted behavior no longer exists; kept `Handle_DefaultParams_RunsPagedAndStatsInParallelAndProjectsResults` and `Handle_ResolvesUserNameFromRequestedBy` unchanged.
- `backend/test/Anela.Heblo.Tests/Features/Article/Pipeline/ArticleFeedbackListValidationPipelineTests.cs` — new integration test building a real `IMediator` (mirroring `ArticleListValidationPipelineTests`) to verify invalid `Page`/`PageSize`/`SortBy` short-circuits with `ErrorCodes.ValidationError` and never reaches the repository, and valid values pass through unmodified.

## Tests
- `ArticleFeedbackListValidationPipelineTests` (8 cases): 5 invalid-input cases (`Page` <= 0, `PageSize` not in `{10,20,50}`, `SortBy` not in the allowlist) assert `Success == false`, `ErrorCode == ValidationError`, and `IArticleRepository.GetFeedbackPagedAsync` is never called; 3 valid-input cases assert the request reaches the handler and returns the exact `Page`/`PageSize` requested.
- `GetArticleFeedbackListHandlerTests` (2 remaining cases): unchanged pass-through behavior tests.

## How to verify
```
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Article"
```
Result: solution build succeeds (0 errors); 122/124 Article-filtered tests pass — the
remaining 2 failures (`ArticleRepositoryFeedbackProjectionSqlTests`) are pre-existing
Testcontainers/PostgreSQL integration tests that require Docker, unrelated to this
change and failing only because Docker is unavailable in this sandbox. All 8 new
pipeline tests and both retained handler tests pass.
`dotnet format Anela.Heblo.sln --verify-no-changes` reports no formatting issues.

## Notes
Followed the task context exactly, including its exact code snippets. No deviations.
Confirmed via `grep -rn "ArticlesController" backend/test` that no other test exercises
`FeedbackList` via HTTP besides the already-reviewed `ArticlesControllerTests` (which
only calls `Generate`).

## PR Summary
Moves `GetArticleFeedbackList` paging/sort validation from ad-hoc handler-side
allowlist-fallback logic to the API boundary, using the same FluentValidation + MediatR
pipeline pattern already applied to `ListArticles`. The controller now binds the
request type directly so validation rules actually run before the handler executes,
and the handler no longer silently falls back to defaults on invalid `PageSize`/`SortBy`
or clamps an invalid `Page`.

This is a deliberate behavior change: callers previously sending an out-of-range
`pageSize` (e.g. 30) or an unrecognized `sortBy` on `GET /api/articles/feedback/list`
got a silent 200 with corrected values; they now get a validation failure response
instead, surfacing the bad input rather than hiding it.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/GetFeedbackList/GetArticleFeedbackListRequestValidator.cs` — new validator
- `backend/src/Anela.Heblo.Application/Features/Article/ArticleModule.cs` — registers validator + pipeline behavior
- `backend/src/Anela.Heblo.API/Controllers/ArticlesController.cs` — `FeedbackList` binds request directly
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/GetFeedbackList/GetArticleFeedbackListHandler.cs` — removed allowlist/clamp logic
- `backend/test/Anela.Heblo.Tests/Article/UseCases/GetArticleFeedbackListHandlerTests.cs` — removed obsolete fallback tests
- `backend/test/Anela.Heblo.Tests/Features/Article/Pipeline/ArticleFeedbackListValidationPipelineTests.cs` — new pipeline integration tests

## Status
DONE
