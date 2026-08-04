# Development: Cap `pageSize` in `ListArticlesHandler`

## Summary

Implemented the plan/design/architecture exactly as approved: clamp `page`/`pageSize` inside
`ListArticlesHandler.Handle` before calling the repository, mirroring the pattern already used by
`GetArticleFeedbackListHandler` in the same module, and echo the clamped values in the response.

## Files changed

### `backend/src/Anela.Heblo.Application/Features/Article/UseCases/ListArticles/ListArticlesHandler.cs`

- Added `var page = Math.Max(1, request.Page);` and `var pageSize = Math.Clamp(request.PageSize, 1, 100);`
  as locals at the top of `Handle`.
- `_repository.GetPagedAsync` is now called with the clamped `page`/`pageSize` locals instead of
  `request.Page`/`request.PageSize`.
- `ListArticlesResponse.Page`/`PageSize` are now populated from the same clamped locals, so the echoed
  values always match what was actually fetched (per FR-1's acceptance criteria and the
  architecture-01.md consistency argument re: `TotalPages`).
- Added a short comment above the clamp explaining *why* it's needed in the handler rather than relying
  on the `[Range]` attributes on `ListArticlesRequest` (per architecture-01.md's risk mitigation: guard
  against a future refactor assuming the attribute is enforced).

No changes to `ListArticlesRequest.cs`, `ArticlesController.cs`, `IArticleRepository.cs`, or
`ArticleRepository.cs` — all out of scope per the plan.

### `backend/test/Anela.Heblo.Tests/Article/UseCases/ListArticlesHandlerTests.cs`

Added four test cases covering the plan's acceptance criteria, following the existing file's style
(Moq `IArticleRepository`, FluentAssertions):

- `Handle_ClampsOversizedPageSizeTo100` — `pageSize=1_000_000` → repository called with `100`, response
  echoes `100`.
- `Handle_ClampsNonPositivePageSizeTo1` (theory: `0`, `-5`) → repository called with `1`, response
  echoes `1`.
- `Handle_ClampsNonPositivePageTo1` (theory: `0`, `-3`) → repository called with page `1`, response
  echoes `1`.
- Existing tests (`Handle_ReturnsMappedListWithPaginationInfo`, `Handle_PassesStatusFilterThroughToRepository`)
  were left unchanged and continue to exercise in-range values (`PageSize=10`, `PageSize=25`), which pass
  through the clamp unchanged — confirming no regression for valid inputs.

## Deviations from the plan

None. Implemented exactly as specified in plan-01.md / design-01.md / architecture-01.md:
- Used `Math.Clamp`, not an allow-list (per architecture-01.md's explicit reasoning).
- Left the `[Range]` attributes on `ListArticlesRequest` in place (per the plan's default resolution to
  the open question), and added the explanatory comment the architecture review's "Gap" section asked
  for, instead of a separate controller-level integration test (no `ArticlesController` test fixture
  exists in this repo's test suite to hang such a test on, and the plan's own test list was already
  judged sufficient — the comment addresses the architecture reviewer's stated concern about a future
  refactor silently reintroducing the gap).

## Verification

**Environment limitation:** this sandbox has no `dotnet` SDK installed (`dotnet` command not found, no
SDK under any checked path), so I could not run `dotnet build`, `dotnet test`, or `dotnet format` here.
The change was reviewed manually line-by-line against the existing, already-passing sibling handler
(`GetArticleFeedbackListHandler`) and the existing test file's conventions; it introduces no new usings,
no new dependencies, and follows patterns (`Math.Max`, `Math.Clamp`, Moq `It.IsAny<T>()`/exact-value
`Verify`, `FluentAssertions .Should()`) already compiling and passing elsewhere in this codebase.

To verify once `dotnet` is available:

```bash
cd backend
dotnet build
dotnet test --filter FullyQualifiedName~ListArticlesHandlerTests
dotnet format --verify-no-changes
```

Expected: build succeeds, all 6 tests in `ListArticlesHandlerTests` pass (2 pre-existing + 4 new,
including the 2 theories with 2 cases each = 8 individual test executions), `dotnet format` reports no
changes needed.

Manual API-level check once running: `GET /api/Articles?pageSize=1000000` should return at most 100
items with `PageSize: 100` in the response body, instead of attempting to fetch 1,000,000 rows.
