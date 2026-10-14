# Implementation: list-articles-boundary-validation

## What was implemented
Moved paging validation for the `ListArticles` use case from the handler to the API
boundary, using the existing `FluentValidation` + `ValidationResultBehavior<,>`
MediatR pipeline pattern already used elsewhere in the codebase (e.g. Analytics,
FileStorage modules). The controller action now binds `ListArticlesRequest`
directly via `[FromQuery]` instead of manually constructing it from individual
parameters, so a `ListArticlesRequestValidator` runs automatically in the MediatR
pipeline before `ListArticlesHandler.Handle` executes. The handler no longer
clamps/sanitizes `Page`/`PageSize` — it trusts the validated request.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/ListArticles/ListArticlesRequestValidator.cs` — new `FluentValidation` validator: `Page >= 1`, `PageSize` in `[1, 100]`.
- `backend/src/Anela.Heblo.Application/Features/Article/ArticleModule.cs` — registers `IValidator<ListArticlesRequest>` and the `ValidationResultBehavior<ListArticlesRequest, ListArticlesResponse>` pipeline behavior.
- `backend/src/Anela.Heblo.API/Controllers/ArticlesController.cs` — `List` action now binds `[FromQuery] ListArticlesRequest request` directly instead of manually constructing the request from `status`/`page`/`pageSize` parameters; removed the now-unused `using Anela.Heblo.Domain.Features.Article;`.
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/ListArticles/ListArticlesHandler.cs` — removed the `Math.Max`/`Math.Clamp` sanitization and its explanatory comment; the handler now passes `request.Page`/`request.PageSize` straight through.
- `backend/test/Anela.Heblo.Tests/Article/UseCases/ListArticlesHandlerTests.cs` — removed the three clamp-assertion tests (`Handle_ClampsOversizedPageSizeTo100`, `Handle_ClampsNonPositivePageSizeTo1`, `Handle_ClampsNonPositivePageTo1`) whose asserted behavior no longer exists; kept the two pass-through tests unchanged.
- `backend/test/Anela.Heblo.Tests/Features/Article/Pipeline/ArticleListValidationPipelineTests.cs` — new integration test building a real `IMediator` (via DI, mirroring the Analytics/FileStorage pattern) to verify invalid `Page`/`PageSize` short-circuits with `ErrorCodes.ValidationError` and never reaches the repository, and valid values pass through unmodified.

## Tests
- `ArticleListValidationPipelineTests` (10 cases): 6 invalid-input cases (`Page` <= 0, `PageSize` <= 0 or > 100) assert `Success == false`, `ErrorCode == ValidationError`, and `IArticleRepository.GetPagedAsync` is never called; 4 valid-input cases assert the request reaches the handler and returns the exact `Page`/`PageSize` requested.
- `ListArticlesHandlerTests` (2 remaining cases): unchanged pass-through behavior tests (`Handle_ReturnsMappedListWithPaginationInfo`, `Handle_PassesStatusFilterThroughToRepository`).

## How to verify
```
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ListArticlesHandlerTests|FullyQualifiedName~ArticleListValidationPipelineTests"
```
Result: build succeeds (0 errors), 12/12 tests pass.

## Notes
Followed the task context exactly, including its exact code snippets. No deviations.
`dotnet format Anela.Heblo.sln --verify-no-changes` reports no formatting issues.

## PR Summary
Moves `ListArticles` paging validation from ad-hoc handler-side clamping to the
API boundary, using the project's existing FluentValidation + MediatR pipeline
pattern. The controller now binds the request type directly so validation
attributes/rules actually run, and the handler no longer silently
sanitizes out-of-range input.

### Changes
- `ListArticlesRequestValidator.cs` — new validator (`Page >= 1`, `PageSize` in `[1, 100]`)
- `ArticleModule.cs` — registers the validator and pipeline behavior
- `ArticlesController.cs` — binds `ListArticlesRequest` directly via `[FromQuery]`
- `ListArticlesHandler.cs` — removed clamping; trusts validated input
- `ListArticlesHandlerTests.cs` — removed obsolete clamp-assertion tests
- `ArticleListValidationPipelineTests.cs` — new pipeline integration tests

## Status
DONE
