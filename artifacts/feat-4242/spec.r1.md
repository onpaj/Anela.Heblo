# Specification: Article paging validation at the API boundary

## Summary
Two Article MediatR handlers (`ListArticlesHandler`, `GetArticleFeedbackListHandler`) currently sanitize/clamp their own `Page`, `PageSize` and `SortBy` inputs because the `ArticlesController` manually constructs the request objects from individual `[FromQuery]` parameters instead of binding the request type directly, so no validation ever runs before the handler. This spec moves that validation to the API boundary — using this codebase's established FluentValidation + MediatR pipeline-behavior pattern — and removes the compensating logic from both handlers so out-of-range input is rejected with a clear error instead of being silently coerced.

## Background
`docs/architecture/development_guidelines.md` and the codebase's existing Packaging/Analytics/FileStorage modules establish the pattern for this system: request DTOs are validated by a `FluentValidation.AbstractValidator<TRequest>`, wired into the request pipeline via a MediatR `IPipelineBehavior<TRequest, TResponse>` registered per request type in that feature's `*Module.cs`, with the controller action binding the request type directly (`[FromQuery] TRequest request`) so query parameters land on the request object before the pipeline runs. Two behaviors exist for this in `Anela.Heblo.Application.Common.Behaviors`:
- `ValidationBehavior<TRequest,TResponse>` — throws `FluentValidation.ValidationException`, caught globally by `ValidationExceptionHandler` and turned into a 400 ProblemDetails response. Used by Packaging/PackingMaterials/Attendance.
- `ValidationResultBehavior<TRequest,TResponse>` (constrained to `TResponse : BaseResponse, new()`) — returns the response type itself with `Success = false` and an `ErrorCodes` value, matching this app's `BaseResponse`/`HandleResponse` convention. Used by Analytics/FileStorage.

Both `ListArticlesResponse` and `GetArticleFeedbackListResponse` already inherit `BaseResponse` and are consumed through `BaseApiController.HandleResponse`, which maps `ErrorCodes` to HTTP status codes. `ValidationResultBehavior` is therefore the better fit for these two Article endpoints: it keeps validation failures inside the same `BaseResponse`/`ErrorCodes` error surface the Article feature already uses everywhere else, rather than introducing a second, inconsistent error shape (ProblemDetails) for just these two endpoints.

The GitHub issue's suggested fix illustrates `[Range]` DataAnnotations attributes directly on the request properties. That would also work (ASP.NET Core `[ApiController]` model validation runs automatically on model-bound parameters), but it is inconsistent with how every comparable paged/sorted list endpoint in this codebase (`GetPackagesRequest`, etc.) validates its input, and DataAnnotations failures surface as raw ModelState/ProblemDetails responses rather than the app's `BaseResponse` shape used elsewhere in the Article feature. This spec adopts the FluentValidation + `ValidationResultBehavior` approach as the primary path, consistent with `GetPackagesRequestValidator` (see `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/GetPackages/GetPackagesRequestValidator.cs` and `PackagingModule.cs`) as the direct precedent for validating `Page`/`PageSize`/`SortBy` on a paged list request that is also `[FromQuery]`-bound directly.

## Functional Requirements

### FR-1: Validate `ListArticlesRequest` at the boundary
Add a `ListArticlesRequestValidator : AbstractValidator<ListArticlesRequest>` in `Application/Features/Article/UseCases/ListArticles/` with:
- `Page`: must be `>= 1`.
- `PageSize`: must be in `[1, 100]` inclusive (matching the current clamp range, which is the widest of the two endpoints' historical limits).

**Acceptance criteria:**
- `GET /api/articles?page=0` (or negative) returns a non-success `ListArticlesResponse` with `ErrorCodes.ValidationError` (or a more specific validation error code if one is introduced) and no data.
- `GET /api/articles?pageSize=0` and `GET /api/articles?pageSize=101` both return the same validation failure shape.
- `GET /api/articles?page=1&pageSize=50` (a value already valid today) continues to succeed and return exactly the requested `page`/`pageSize` unchanged.
- `GET /api/articles` with no paging params defaults to `page=1`, `pageSize=20` exactly as today (defaults come from `ListArticlesRequest`'s property initializers, unaffected by this change).

### FR-2: Validate `GetArticleFeedbackListRequest` at the boundary
Add a `GetArticleFeedbackListRequestValidator : AbstractValidator<GetArticleFeedbackListRequest>` in `Application/Features/Article/UseCases/GetFeedbackList/` with:
- `Page`: must be `>= 1`.
- `PageSize`: must be one of `{10, 20, 50}` (preserving the existing allowlist semantics — this is a deliberate behavior preservation, not a widening to `[1,100]`; the two endpoints have historically had different constraints, and this spec does not attempt to unify them since the issue does not ask for that and no caller/UI dependency on either exact range was investigated).
- `SortBy`: must be one of `{"CreatedAt", "PrecisionScore", "StyleScore"}` (the same allowlist currently enforced in the handler).

**Acceptance criteria:**
- `GET /api/articles/feedback/list?pageSize=30` returns a validation failure (today it silently returns `pageSize=20` with no indication).
- `GET /api/articles/feedback/list?sortBy=Bogus` returns a validation failure (today it silently falls back to `CreatedAt`).
- `GET /api/articles/feedback/list?page=0` returns a validation failure.
- `GET /api/articles/feedback/list?pageSize=10` / `20` / `50` and `sortBy=CreatedAt` / `PrecisionScore` / `StyleScore` (the currently-allowed values) continue to succeed unchanged.
- Omitting `page`, `pageSize`, or `sortBy` still defaults to `1`, `20`, `"CreatedAt"` respectively, unaffected by this change.

### FR-3: Bind both controller actions directly to their request types
Change `ArticlesController.List` and `ArticlesController.FeedbackList` to accept `[FromQuery] ListArticlesRequest request` and `[FromQuery] GetArticleFeedbackListRequest request` respectively (mirroring `PackagingController`'s `GetPackages` action), instead of individual `[FromQuery]` scalar parameters manually assembled into a new request object. `status` on `ListArticlesRequest` and `hasFeedback`/`requestedBy`/`sortDescending` on `GetArticleFeedbackListRequest` must continue to bind correctly from query string (nullable/bool query binding works the same way whether bound individually or as part of a model).

**Acceptance criteria:**
- Existing query parameter names (`status`, `page`, `pageSize`, `hasFeedback`, `requestedBy`, `sortBy`, `sortDescending`) are unchanged and continue to bind to the same request properties — this is purely an internal binding-mechanism change, not an API contract change.
- No `CancellationToken ct = default` parameter regression: the action signatures keep accepting cancellation the same way `PackagingController.GetPackages` does (`CancellationToken ct` as a separate action parameter alongside the bound request).

### FR-4: Remove compensating logic from both handlers
- `ListArticlesHandler`: delete the `Math.Max`/`Math.Clamp` lines and the comment explaining the workaround; use `request.Page` and `request.PageSize` directly.
- `GetArticleFeedbackListHandler`: delete the `AllowedPageSizes`/`AllowedSortColumns` static arrays and the three compensating lines; use `request.Page`, `request.PageSize`, `request.SortBy` directly.

**Acceptance criteria:**
- Neither handler contains any clamping, allowlist-checking, or defaulting logic for `Page`, `PageSize`, or `SortBy` after this change.
- Both handlers' existing unit tests (if any target the clamping behavior) are updated to reflect that out-of-range input is now rejected before reaching the handler, not silently corrected inside it.

## Non-Functional Requirements

### NFR-1: Performance
No measurable impact expected. FluentValidation on two scalar/string fields per request is negligible relative to the existing repository query cost. No new DB round-trips are introduced.

### NFR-2: Security
No new attack surface. This change tightens input handling (rejects invalid input instead of silently coercing it), which is a net security/robustness improvement — it prevents unbounded or unexpected `pageSize`/`sortBy` values from reaching the repository layer.

### NFR-3: Backward compatibility
Existing callers sending values that were previously *silently corrected* (e.g., `pageSize=30` on the feedback-list endpoint, or `page=-1` on either endpoint) will now receive a validation error instead of a 200 response with corrected values. This is the explicit intent of the issue (surfacing caller bugs instead of hiding them) and must be called out in the PR description as a behavior change, even though no known caller currently relies on it (not verified against frontend usage in this spec — see Open Questions).

## Data Model
No data model changes. No new entities, no persistence changes. `IArticleRepository.GetPagedAsync` and `GetFeedbackPagedAsync` signatures are unchanged; they still receive `page`/`pageSize` (and `sortBy` for feedback), just now guaranteed-valid by the time the handler calls them.

## API / Interface Design
- `GET /api/articles` — query params unchanged (`status`, `page`, `pageSize`); now validated before the handler runs. Invalid input yields a non-2xx response carrying `ListArticlesResponse { Success = false, ErrorCode = ErrorCodes.ValidationError, ... }` instead of silently succeeding with clamped values.
- `GET /api/articles/feedback/list` — query params unchanged (`hasFeedback`, `requestedBy`, `sortBy`, `sortDescending`, `page`, `pageSize`); same validation-failure shape as above on invalid input.
- Both endpoints' success-path response shape (`Items`, `TotalCount`, `Page`, `PageSize`, and for feedback also `Stats`) is unchanged.
- New files:
  - `Application/Features/Article/UseCases/ListArticles/ListArticlesRequestValidator.cs`
  - `Application/Features/Article/UseCases/GetFeedbackList/GetArticleFeedbackListRequestValidator.cs`
- Modified files:
  - `API/Controllers/ArticlesController.cs` (bind request types directly for `List` and `FeedbackList` actions)
  - `Application/Features/Article/UseCases/ListArticles/ListArticlesHandler.cs` (remove clamping)
  - `Application/Features/Article/UseCases/GetFeedbackList/GetArticleFeedbackListHandler.cs` (remove allowlist logic)
  - `Application/Features/Article/ArticleModule.cs` (register the two validators and their `IPipelineBehavior<,>` closed-generic registrations using `ValidationResultBehavior<,>`, mirroring `PackagingModule.AddPackagingModule`)

## Dependencies
- `FluentValidation` (already a dependency of `Anela.Heblo.Application`, already used elsewhere).
- `Anela.Heblo.Application.Common.Behaviors.ValidationResultBehavior<TRequest,TResponse>` (existing, reused as-is — no changes to this shared behavior).
- No new external dependencies.

## Out of Scope
- Unifying the two endpoints' differing `PageSize` constraints (`[1,100]` vs `{10,20,50}`) into one shared rule — the issue calls out the inconsistency as a symptom of the underlying bug (validation done ad hoc per handler), but does not ask for the ranges themselves to be unified, and doing so could be a breaking change for any caller currently relying on the wider `ListArticles` range. Each endpoint keeps its current effective range, just enforced via validation instead of clamping.
- Changing the response shape/error-code contract for validation failures beyond using the existing `BaseResponse`/`ErrorCodes.ValidationError` convention (e.g., no move to RFC 7807 ProblemDetails, no field-level validation error details) — this is an existing app-wide convention decision, not something to relitigate for two endpoints.
- Any other Article endpoints not named in the issue (`Generate`, `GetById`, `GetTrace`, `SubmitFeedback`, `BackfillRequestedBy`) — out of scope, not touched.
- Frontend/TypeScript client changes — the API request/response *shape* (query params, JSON body) is unchanged; only server-side validation timing and enforcement changes, so no regeneration of the OpenAPI client should be functionally required beyond whatever the build step does automatically. If FluentValidation error metadata changes the generated OpenAPI schema in an unexpected way, that is a build-time detail for the implementer to verify, not a design decision for this spec.

## Open Questions
None.

(Note for the architect review: this spec adopts the FluentValidation + `ValidationResultBehavior` approach — matching this codebase's established pattern in `PackagingModule`/`AnalyticsModule`/`FileStorageModule` — as the codebase-consistent interpretation of the issue's suggested fix, rather than the literal `[Range]` DataAnnotations sample shown in the issue body. This is a considered assumption, not a blocking question.)

## Status: COMPLETE
