# Architecture Assessment: Cap `pageSize` in `ListArticlesHandler`

## Verdict

Approve the plan/design as written, with one addition (see "Gap" below). This is a same-day,
low-risk, single-file fix. It requires no new component, no interface change, and no architectural
decision beyond "which of two existing patterns to reuse" — which the plan already answered
correctly. I re-verified every factual claim in `plan-01.md` and `design-01.md` against current
source; all hold.

## Alignment with existing patterns

Confirmed by direct read of source (not just the plan's account):

- `ListArticlesHandler.cs` (current, 40 lines) forwards `request.Page`/`request.PageSize` unclamped
  to `IArticleRepository.GetPagedAsync`, and echoes the raw values in the response. Matches the
  finding exactly.
- `ListArticlesRequest.cs` **does** carry `[Range(1, int.MaxValue)]` on `Page` and `[Range(1, 100)]`
  on `PageSize` — the finding's claim of "no `[Range]` attribute" is stale, as the plan already
  flagged.
- `ArticlesController.List` binds three loose primitives (`status`, `page`, `pageSize` as plain
  `int`/`ArticleStatus?`) and hand-constructs the `ListArticlesRequest` inside the action body. Since
  `[ApiController]` automatic validation only inspects the action's own bound parameters, the
  `[Range]` attributes on the manually-constructed object are never evaluated by ASP.NET Core.
- `ValidationBehavior<TRequest, TResponse>` (`Common/Behaviors/ValidationBehavior.cs`) only resolves
  and runs `IValidator<TRequest>` (FluentValidation) — there is no `ValidateAnnotatedProperties`-style
  step in the MediatR pipeline. No `AbstractValidator<ListArticlesRequest>` exists. So the `[Range]`
  attributes are inert for this request end-to-end, confirming the plan's root-cause analysis.
- `GetArticleFeedbackListHandler.Handle` (`GetFeedbackList/GetArticleFeedbackListHandler.cs:28-29`)
  is the working sibling pattern in the same module: `page = Math.Max(1, request.Page)` and an
  allow-list clamp for `pageSize`, both computed as **locals**, both used for the repository call
  *and* echoed back in the response. This is the correct model to mirror.

Two viable enforcement points exist in this codebase's conventions — handler-side clamp (used by
`GetArticleFeedbackListHandler`) vs. bind-the-request-object-and-check-`ModelState` (used by
`PurchaseOrdersController.GetPurchaseOrders`). The plan's choice of the handler-side clamp is
correct: it is self-contained, touches one file's logic, and does not ripple into the OpenAPI
contract or generated TypeScript client the way changing the controller's binding style would.
Restructuring the controller is a legitimate follow-up but is out of proportion to a defect fix and
would itself need separate review (route/query-param OpenAPI shape is a public contract point per
`docs/development/api-client-generation.md`).

## Proposed architecture

No new components. This is a one-line-of-intent change confined to `ListArticlesHandler.Handle`:

1. Compute `page = Math.Max(1, request.Page)` and `pageSize = Math.Clamp(request.PageSize, 1, 100)`
   as locals at the top of `Handle`.
2. Pass the locals — not `request.Page`/`request.PageSize` — to `_repository.GetPagedAsync`.
3. Populate `ListArticlesResponse.Page`/`PageSize` from the same locals.

**Decision: clamp via `Math.Clamp`, not an allow-list.** `GetArticleFeedbackListHandler` uses an
allow-list (`[10, 20, 50]`) because its consumer is a fixed-tier UI dropdown. `ListArticles` has no
such UI constraint documented — the spec (`docs/features/article-generation.md` §7) states "default
20, max 100" as a range, not a discrete set. An allow-list here would silently reject e.g.
`pageSize=30` by falling back to 20, which is a *behavior change* the finding never asked for and
the spec doesn't justify. `Math.Clamp(x, 1, 100)` enforces exactly the documented bound while
leaving every currently-valid request untouched — the minimal correct fix. Do not import the
allow-list pattern here; the two handlers have different contracts even though they live in the same
module.

**Decision: echo clamped values in the response, not raw request values.** This is a deliberate,
correctly-flagged behavior change (design-01.md's table). It's necessary, not optional: `TotalCount`
comes from a query run with the clamped `pageSize`, so if the response echoed
`request.PageSize=1000000` while `TotalCount` reflects a 100-row fetch, a client computing
`TotalPages = ceil(TotalCount / PageSize)` would silently get a nonsensical value. Consistency
between "what was echoed" and "what was actually fetched" is the correct invariant, and it's exactly
what `GetArticleFeedbackListHandler` already guarantees for its endpoint. Surface this in the PR
description as called out in the plan — it is a response-value change even though the JSON schema
is unchanged.

## Implementation guidance

- **File to change:** `backend/src/Anela.Heblo.Application/Features/Article/UseCases/ListArticles/ListArticlesHandler.cs`
  only. No change to `ListArticlesRequest.cs`, `ArticlesController.cs`, `IArticleRepository.cs`, or
  `ArticleRepository.cs`.
- **Contract:** `IArticleRepository.GetPagedAsync(ArticleStatus?, int page, int pageSize, CancellationToken)`
  is unchanged; only the arguments the handler passes change.
- **Data flow:** `ArticlesController.List` → `ListArticlesRequest` → `ListArticlesHandler.Handle`
  computes `page`/`pageSize` locals → `IArticleRepository.GetPagedAsync(status, page, pageSize, ct)`
  → `ListArticlesResponse` populated from the same locals.
- **Tests:** extend the existing `backend/test/Anela.Heblo.Tests/Article/UseCases/ListArticlesHandlerTests.cs`
  (already present, already mocks `IArticleRepository` and asserts on call arguments and response
  fields — no new test infrastructure needed). Add cases for: oversized `pageSize` clamps to 100,
  zero/negative `pageSize` clamps to 1, zero/negative `page` clamps to 1, and reconfirm the two
  existing tests (`Handle_ReturnsMappedListWithPaginationInfo`,
  `Handle_PassesStatusFilterThroughToRepository`) still pass with in-range inputs unchanged.
- **`[Range]` attributes on `ListArticlesRequest`:** leave in place, per the plan's default. They are
  inert today but harmless, and document intent for a future reader. Do not attempt to "fix" their
  inertness (e.g., by wiring `ValidateAnnotatedProperties` or rebinding the controller) in this
  change — that is a separate, larger decision (see Risks).

## Gap in the plan: add one regression-guard test

The plan's test list covers the handler's clamp logic thoroughly but does not include a test at the
`ArticlesController` level. Since the actual bug lived in the *controller's manual construction* of
`ListArticlesRequest bypassing model validation, not in the handler alone, I'd add one integration-style
assertion (if a controller test fixture already exists for `ArticlesController`) or, at minimum, a
handler test comment noting *why* the clamp must live in the handler and not rely on the `[Range]`
attribute — so a future refactor of the controller's binding style doesn't reintroduce the gap by
assuming validation already happens upstream. This is a small addition to FR-1's acceptance criteria,
not a new requirement — check for `backend/test/Anela.Heblo.Tests.Integration` or similar before
deciding whether it's worth the extra test versus a comment.

## Risks and mitigations

- **Risk:** A future developer sees `[Range(1, 100)]` on `PageSize` and assumes it's enforced,
  then removes the handler-side clamp during a refactor (e.g., "this is now redundant").
  **Mitigation:** the plan already asks for this discrepancy to be called out in the PR description;
  I'd go further and add a one-line comment directly above the clamp in `ListArticlesHandler`
  explaining that the `[Range]` attribute is not evaluated for this endpoint (binding bypasses
  `ModelState`) so the reader doesn't need to rediscover this by reading `ValidationBehavior.cs` and
  the controller together.
- **Risk (accepted, out of scope):** the OpenAPI-generated documentation still shows `pageSize` as an
  unconstrained `int` in the controller signature, since the constraint now lives in handler logic,
  not in a validated/annotated bound parameter. This was already flagged as a known gap by the
  finding and explicitly deferred by the plan (open question 2) — agreed, not worth doing here.
- **No migration, no data risk, no rollback complexity** — this is a pure in-process computation
  change with no persisted state.

## Prerequisites before implementation

None. No dependency changes, no schema changes, no coordination with other in-flight work needed.
Implementation can start immediately following the plan's rough-plan steps 1–5.
