# Plan: Cap `pageSize` in `ListArticlesHandler`

## Summary

`ListArticlesHandler.Handle` forwards `request.PageSize` straight to `IArticleRepository.GetPagedAsync`
with no effective upper bound, so `GET /api/Articles?pageSize=1000000` triggers an unbounded `Skip/Take`
query and returns an unbounded number of rows (each carrying `HtmlContent`, potentially several KB).
The fix is to clamp `page`/`pageSize` in the handler, mirroring the pattern already used by
`GetArticleFeedbackListHandler` in the same module.

## Context

**Important correction to the filed finding:** the finding states "There is no `[Range]` attribute on
the parameter." That is no longer accurate — `ListArticlesRequest.PageSize` already carries
`[Range(1, 100)]` (and `Page` carries `[Range(1, int.MaxValue)]`), added in the original Article
Generation feature commit (`d06bf64c`, 2026-05-05), predating the finding (filed 2026-07-24).

However, the underlying vulnerability is real and the finding's core conclusion still holds, for a
subtler reason: **the attribute is present but never evaluated.**

- `ArticlesController.List` does not bind `[FromQuery] ListArticlesRequest request` directly — it binds
  three separate primitives (`status`, `page`, `pageSize`) and manually constructs
  `new ListArticlesRequest { ... }` inside the action body (`ArticlesController.cs:56-69`). ASP.NET Core's
  `[ApiController]` automatic model validation only inspects the action's own bound parameters (plain
  `int`s here, no attributes) — it never touches the manually-constructed object, so `ModelState` never
  sees the `[Range]` attribute on `PageSize`.
- This project's MediatR pipeline (`ValidationBehavior`, `ValidationResultBehavior` in
  `Common/Behaviors/`) only executes **FluentValidation** `IValidator<TRequest>` implementations. Neither
  attribute-based validation nor a FluentValidation validator exists for `ListArticlesRequest` (confirmed:
  no `AbstractValidator<ListArticlesRequest>` anywhere in the codebase).
- So today, `[Range(1, 100)]` on `ListArticlesRequest.PageSize` is dead code for this endpoint. A request
  with `pageSize=1000000` sails through untouched all the way to `GetPagedAsync` → `.Take(pageSize)`.

For contrast, the codebase has two working patterns elsewhere:
1. **Handler-side clamp** — `GetArticleFeedbackListHandler` (`GetFeedbackList/GetArticleFeedbackListHandler.cs:28-29`)
   clamps `page`/`pageSize` in the handler body regardless of what validation ran upstream.
2. **Bind-the-request-object + `ModelState.IsValid`** — `PurchaseOrdersController.GetPurchaseOrders`
   (`PurchaseOrdersController.cs:31-37`) binds `[FromQuery] GetPurchaseOrdersRequest request` directly,
   which makes `[ApiController]` automatic model validation (and thus `[Range]`) actually take effect,
   paired with an explicit `ModelState.IsValid` check.

Changing `ArticlesController.List`'s parameter binding style (pattern 2) would also restructure its
OpenAPI-generated signature (query params → request object), rippling into the generated TypeScript
client and any frontend callers — out of proportion for this fix. The handler-side clamp (pattern 1) is
self-contained, matches an existing sibling handler in the same module, and requires no controller or
contract changes.

## Functional requirements

**FR-1: Clamp `page` and `pageSize` inside `ListArticlesHandler.Handle` before calling the repository.**
- `page` is clamped to a minimum of 1 (`Math.Max(1, request.Page)`).
- `pageSize` is clamped to the range `[1, 100]` (`Math.Clamp(request.PageSize, 1, 100)`), matching the
  cap documented in `docs/features/article-generation.md` section 7 ("max 100").
- Acceptance criteria:
  - `pageSize=1000000` → repository is called with `pageSize=100`, not `1000000`.
  - `pageSize=0` or negative → repository is called with `pageSize=1`.
  - `page=0` or negative → repository is called with `page=1`.
  - A valid `pageSize` (e.g. 10, 20, 50, 100) is passed through unchanged.
  - The response's echoed `Page`/`PageSize` fields reflect the **clamped** values actually used (as
    `GetArticleFeedbackListHandler` already does), not the raw request values — this keeps `TotalPages`
    consistent with what was actually fetched. Note this is an observable behavior change from today's
    `ListArticlesResponse` (currently echoes raw `request.Page`/`request.PageSize`
    per `ListArticlesHandler.cs:37-38`); call this out in the PR description.

**FR-2 (nice-to-have, not required for the fix): decide the fate of the now-inert `[Range]` attributes
on `ListArticlesRequest`.**
- Leaving them is harmless (they cost nothing and self-document intent) but may mislead a future reader
  into believing they are enforced. No functional acceptance criteria — resolve via the open question
  below, default to leaving them in place with the clamp added.

## Non-functional requirements

- **Performance / resource protection**: this is the actual point of the fix — bound the maximum rows
  fetched per request to 100, preventing large table scans and large in-memory result sets
  (`HtmlContent` per article).
- No new dependencies, no schema/migration changes, no OpenAPI contract change (response shape and
  controller route/parameters are unchanged; only the numeric values flowing through change).

## Data model

No entity or schema changes. Existing `Article` entity and `IArticleRepository.GetPagedAsync` signature
are unchanged.

## Interfaces

No new or changed endpoints. `GET /api/Articles?status=&page=&pageSize=` keeps its exact current
signature; only the internal handling of out-of-range `pageSize`/`page` values changes.

## Dependencies and scope

**In scope:**
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/ListArticles/ListArticlesHandler.cs` —
  add the clamp.
- `backend/test/Anela.Heblo.Tests/Article/UseCases/ListArticlesHandlerTests.cs` — add test cases for the
  clamp (oversized `pageSize`, zero/negative `pageSize`, zero/negative `page`, and confirm the response
  echoes clamped values).

**Out of scope:**
- Changing `ArticlesController.List`'s parameter binding style or adding a FluentValidation validator —
  the handler-side clamp is sufficient and lower-risk; do not also refactor the controller in this pass.
- Any change to `GetArticleFeedbackListHandler` (already correct) or to `IArticleRepository`/`ArticleRepository`.
- Removing the `[Range]` attributes from `ListArticlesRequest` unless the open question below is resolved
  in favor of removal.
- OpenAPI/Swagger documentation accuracy for the `pageSize` constraint — separate concern, not touched by
  this fix (the query parameters are plain `int`s in the controller signature either way).

## Rough plan

1. In `ListArticlesHandler.Handle`, compute `var page = Math.Max(1, request.Page);` and
   `var pageSize = Math.Clamp(request.PageSize, 1, 100);`, and pass `page`/`pageSize` (not
   `request.Page`/`request.PageSize`) to `_repository.GetPagedAsync`.
2. Update the returned `ListArticlesResponse` to echo the clamped `page`/`pageSize`, not the raw request
   values (matches `GetArticleFeedbackListHandler`'s behavior).
3. Add unit tests to `ListArticlesHandlerTests.cs`:
   - oversized `pageSize` (e.g. 1_000_000) → repository called with 100, response echoes 100.
   - `pageSize = 0` and negative → repository called with 1.
   - `page = 0` and negative → repository called with 1.
   - existing in-range cases continue to pass unchanged.
4. Run `dotnet build` and the touched test project; confirm all Article-module tests pass.
5. `dotnet format` before finishing, per repo validation checklist.

## Open questions

- **Should the now-ineffective `[Range]` attributes on `ListArticlesRequest` be removed, or left as
  documentation-only?** Default: leave them in place — they're harmless, and removing them could be
  read as regressing intent if a future validator wiring pass picks them up via
  `ValidateDataAnnotations`. Flag this discrepancy in the PR description so reviewers aren't confused
  about why a `[Range]`-annotated property still needs a manual clamp.
- **Should `ArticlesController.List` be refactored to bind `ListArticlesRequest` directly (pattern used
  by `PurchaseOrdersController`), making the `[Range]` attributes actually load-bearing?** Default: no,
  out of scope for this fix — it's a larger, separately-reviewable change to the controller's OpenAPI
  contract and generated TypeScript client. Worth a follow-up backlog item, not part of this fix.
