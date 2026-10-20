# Architecture Review: Article paging validation at the API boundary

## Skip Design: true

Backend-only change: two request validators, two `[FromQuery]` binding-mechanism edits, two DI registrations, and removal of dead compensating code from two handlers. No new or changed UI component, screen, or visual design decision. No frontend contract change (query parameter names, response shapes, and defaults are all unchanged).

## Architectural Fit Assessment

This fits an existing, well-established pattern in this codebase and does not introduce anything new at the system level. Verified by reading:

- `backend/src/Anela.Heblo.Application/Common/Behaviors/ValidationBehavior.cs` and `ValidationResultBehavior.cs` — two MediatR `IPipelineBehavior<TRequest,TResponse>` implementations already exist for exactly this purpose (run FluentValidation before the handler executes).
- `backend/src/Anela.Heblo.Application/Features/Packaging/UseCases/GetPackages/GetPackagesRequestValidator.cs` + `PackagingModule.cs` — the direct precedent: a paged list request validator with `PageNumber >= 1`, `PageSize` range, and a `SortBy` allowlist via `.Must(...)`, wired with `ValidationBehavior`, on a controller action that binds `[FromQuery] GetPackagesRequest request` directly (`PackagingController.cs:170`). This is structurally the same shape as `ListArticlesRequest`/`GetArticleFeedbackListRequest`.
- `backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs` and `FileStorage/FileStorageModule.cs` — precedent for `ValidationResultBehavior<TRequest,TResponse>` (the non-throwing variant, constrained to `TResponse : BaseResponse, new()`), which returns the response itself with `Success = false` and an `ErrorCodes` value instead of throwing.
- `ListArticlesResponse` and `GetArticleFeedbackListResponse` both already inherit `BaseResponse` and flow through `BaseApiController.HandleResponse`, which maps `ErrorCodes` → HTTP status via `HttpStatusCodeAttribute`. `ErrorCodes.ValidationError = 0001` already exists.

**Confirmed and adopted: the spec's choice of `ValidationResultBehavior` (not `ValidationBehavior`) is correct.** Using the throwing `ValidationBehavior` here would make these two Article endpoints return a ProblemDetails-shaped 400 while every other Article endpoint (including the same two on their non-validation error paths, e.g. any future domain error) returns a `BaseResponse`-shaped body through `HandleResponse`. That would be a locally-inconsistent error contract within the same controller. `ValidationResultBehavior` keeps the response envelope uniform across the whole `ArticlesController`.

The `[Range]`/DataAnnotations approach shown in the GitHub issue's suggested-fix sample was considered and rejected for the same reason: it is not the pattern used anywhere else in this codebase for MediatR request validation, and it would bypass `BaseResponse`/`HandleResponse` entirely (ASP.NET Core's automatic `[ApiController]` model validation returns its own ProblemDetails 400 before MediatR is even invoked). The issue's *diagnosis* (validate at the boundary, stop the handler from compensating) is correct and fully addressed by this design; only the *mechanism* differs from the issue's illustrative snippet, in favor of the mechanism this codebase already standardizes on.

## Proposed Architecture

### Component Overview

```
HTTP GET /api/articles?page=..&pageSize=..&status=..
        │
        ▼
ArticlesController.List([FromQuery] ListArticlesRequest request, ct)
        │  (ASP.NET Core model binding populates `request` directly;
        │   no more manual `new ListArticlesRequest { ... }`)
        ▼
IMediator.Send(request)
        │
        ▼
MediatR pipeline: ValidationResultBehavior<ListArticlesRequest, ListArticlesResponse>
        │  runs ListArticlesRequestValidator
        │
        ├─ invalid ──▶ returns ListArticlesResponse { Success=false, ErrorCode=ValidationError } ──▶ HandleResponse ──▶ 400
        │
        └─ valid ──▶ next() ──▶ ListArticlesHandler.Handle(request)
                                   (no clamping — uses request.Page / request.PageSize as-is)
                                   ──▶ IArticleRepository.GetPagedAsync(...)
```

The `GetArticleFeedbackListRequest` / `GetArticleFeedbackListHandler` flow is structurally identical, with `GetArticleFeedbackListRequestValidator` additionally constraining `SortBy` to the existing allowlist.

### Key Design Decisions

#### Decision 1: `ValidationResultBehavior` vs `ValidationBehavior`
**Options considered:**
(a) `ValidationBehavior` (throws `FluentValidation.ValidationException`, globally caught by `ValidationExceptionHandler` → ProblemDetails 400) — used by Packaging/PackingMaterials/Attendance.
(b) `ValidationResultBehavior` (returns `TResponse` with `Success=false`, `ErrorCode`) — used by Analytics/FileStorage.
(c) DataAnnotations `[Range]` + native ASP.NET Core model validation (the issue's illustrative sample).

**Chosen approach:** (b), `ValidationResultBehavior`.

**Rationale:** Both `ListArticlesResponse` and `GetArticleFeedbackListResponse` are `BaseResponse` subclasses already consumed via `HandleResponse`. (b) is the only option that keeps validation failures in the same response envelope as every other error case these two endpoints can already produce. (a) and (c) would both introduce a second, ProblemDetails-shaped error contract used only by these two Article endpoints, which is a worse local outcome even though (a) is the more common choice codebase-wide (it's simply used by feature modules whose responses are *not* `BaseResponse`).

#### Decision 2: Preserve each endpoint's existing constraint values, don't unify them
**Options considered:** Unify `PageSize` limits across both endpoints (e.g., both `[1,100]`, or both `{10,20,50}`) vs. keep each endpoint's current effective range.

**Chosen approach:** Keep each endpoint's current range — `ListArticles`: `PageSize ∈ [1,100]`; `GetArticleFeedbackList`: `PageSize ∈ {10,20,50}`, `SortBy ∈ {CreatedAt, PrecisionScore, StyleScore}`.

**Rationale:** The issue's "why it matters" section names the inconsistency as a symptom of the validation being ad hoc per-handler, not as a defect to fix in its own right. No suggested fix line asks for unification, and doing so risks a silent behavior change for any caller currently depending on `ListArticles`'s wider range (e.g. a caller using `pageSize=100` today would break if unified down to `{10,20,50}`). Fixing *where* validation happens and *making it explicit/rejecting* is the full scope; changing *what* is valid is a separate, unrequested decision.

## Implementation Guidance

### Directory / Module Structure
New files (co-located with their request types, matching `GetPackagesRequestValidator`'s placement alongside `GetPackagesRequest`):
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/ListArticles/ListArticlesRequestValidator.cs`
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/GetFeedbackList/GetArticleFeedbackListRequestValidator.cs`

Modified files:
- `backend/src/Anela.Heblo.API/Controllers/ArticlesController.cs`
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/ListArticles/ListArticlesHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/GetFeedbackList/GetArticleFeedbackListHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/Article/ArticleModule.cs`
- `backend/test/Anela.Heblo.Tests/Article/UseCases/ListArticlesHandlerTests.cs` (remove/replace clamp-assertion tests — see Risks)
- `backend/test/Anela.Heblo.Tests/Article/UseCases/GetArticleFeedbackListHandlerTests.cs` (remove/replace allowlist-assertion tests — see Risks)

### Interfaces and Contracts

```csharp
// Application/Features/Article/UseCases/ListArticles/ListArticlesRequestValidator.cs
public class ListArticlesRequestValidator : AbstractValidator<ListArticlesRequest>
{
    public ListArticlesRequestValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}
```

```csharp
// Application/Features/Article/UseCases/GetFeedbackList/GetArticleFeedbackListRequestValidator.cs
public class GetArticleFeedbackListRequestValidator : AbstractValidator<GetArticleFeedbackListRequest>
{
    private static readonly int[] AllowedPageSizes = [10, 20, 50];
    private static readonly string[] AllowedSortColumns = ["CreatedAt", "PrecisionScore", "StyleScore"];

    public GetArticleFeedbackListRequestValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).Must(AllowedPageSizes.Contains)
            .WithMessage($"PageSize must be one of: {string.Join(", ", AllowedPageSizes)}");
        RuleFor(x => x.SortBy).Must(AllowedSortColumns.Contains)
            .WithMessage($"SortBy must be one of: {string.Join(", ", AllowedSortColumns)}");
    }
}
```

`ArticleModule.AddArticleModule` gains (mirroring `PackagingModule`/`AnalyticsModule` exactly):
```csharp
services.AddScoped<IValidator<ListArticlesRequest>, ListArticlesRequestValidator>();
services.AddScoped<
    IPipelineBehavior<ListArticlesRequest, ListArticlesResponse>,
    ValidationResultBehavior<ListArticlesRequest, ListArticlesResponse>>();

services.AddScoped<IValidator<GetArticleFeedbackListRequest>, GetArticleFeedbackListRequestValidator>();
services.AddScoped<
    IPipelineBehavior<GetArticleFeedbackListRequest, GetArticleFeedbackListResponse>,
    ValidationResultBehavior<GetArticleFeedbackListRequest, GetArticleFeedbackListResponse>>();
```
Requires `using Anela.Heblo.Application.Common.Behaviors;` and `using FluentValidation;` added to `ArticleModule.cs`.

`ArticlesController.cs` action signatures become:
```csharp
[HttpGet]
public async Task<ActionResult<ListArticlesResponse>> List(
    [FromQuery] ListArticlesRequest request,
    CancellationToken ct = default)
{
    var result = await _mediator.Send(request, ct);
    return HandleResponse(result);
}
```
```csharp
[HttpGet("feedback/list")]
[FeatureAuthorize(Feature.Marketing_Article, AccessLevel.Write)]
public async Task<ActionResult<GetArticleFeedbackListResponse>> FeedbackList(
    [FromQuery] GetArticleFeedbackListRequest request,
    CancellationToken ct = default)
{
    var result = await _mediator.Send(request, ct);
    return HandleResponse(result);
}
```
(This exactly mirrors `PackagingController.GetPackages`'s existing `[FromQuery] GetPackagesRequest request` pattern — no new binding technique for this codebase.)

Handlers lose their compensating lines entirely:
- `ListArticlesHandler.Handle`: use `request.Page` / `request.PageSize` directly in the call to `_repository.GetPagedAsync(...)`, and in the returned `Page`/`PageSize` on the response.
- `GetArticleFeedbackListHandler.Handle`: use `request.Page` / `request.PageSize` / `request.SortBy` directly; delete the `AllowedPageSizes`/`AllowedSortColumns` static arrays (they move into the validator).

### Data Flow
Query string → ASP.NET Core model binder → `TRequest` instance → `IMediator.Send` → MediatR pipeline resolves `IPipelineBehavior<TRequest,TResponse>` → `ValidationResultBehavior` resolves `IEnumerable<IValidator<TRequest>>`, runs them → on failure, short-circuits and returns a `new TResponse { Success=false, ErrorCode=... }` without ever constructing/calling the handler → on success, calls `next()` which invokes the handler → handler trusts all fields on `request` are valid.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Existing handler unit tests assert the now-removed clamping/allowlist behavior directly (`ListArticlesHandlerTests.Handle_ClampsOversizedPageSizeTo100`, `Handle_ClampsNonPositivePageSizeTo1`, `Handle_ClampsNonPositivePageTo1`; `GetArticleFeedbackListHandlerTests.Handle_UnknownSortBy_FallsBackToCreatedAt`, `Handle_PageSizeOutsideAllowlist_FallsBackTo20`) — these will fail to compile/pass once the handler code is deleted, since they call the handler directly with out-of-range values and assert it self-corrects. | Medium | Remove these handler-level tests (the behavior they assert no longer exists in the handler) and add validator-level tests (`ListArticlesRequestValidatorTests`, `GetArticleFeedbackListRequestValidatorTests`) asserting the same boundary values are now rejected. Planner must include this as an explicit task, not an afterthought — a red/deleted test is the cheapest way to silently regress this fix. |
| Breaking change for any real caller currently sending out-of-range values and relying on silent correction (e.g. a caller passing `pageSize=30` to the feedback-list endpoint and expecting `20` back rather than an error). | Low | Not verified against frontend usage as part of this review (out of scope per spec). The issue explicitly wants this "silent failure" to stop being silent, so surfacing it is the intended outcome, not a regression to avoid. Call out as a behavior change in the PR description. If the frontend is later found to send such values, that is a separate frontend bug to fix, not a reason to keep server-side silent coercion. |
| `IPipelineBehavior<,>` registrations are per-closed-generic-type in this codebase (no global open-generic registration exists in `ApplicationModule.cs`) — easy to forget one of the two registrations, in which case the validator is registered but never runs (MediatR would just never resolve the behavior for that request/response pair, and validation would silently do nothing). | Medium | Verified by reading `ApplicationModule.cs:67` (only `AddMediatR(...)`, no `services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>))`). Planner/developer must register both the `IValidator<TRequest>` and the `IPipelineBehavior<TRequest,TResponse>` for each of the two request/response pairs, exactly as shown above — a build won't catch a missing pipeline registration, only a passing/failing validation test will. |
| `ArticlesController.List`'s current signature has `status` as a *nullable* enum default parameter (`ArticleStatus? status = null`) directly on the action; after binding `ListArticlesRequest` directly, `Status` must still bind correctly from `?status=Failed` as a property on the model. | Low | This is standard ASP.NET Core model binding behavior (nullable enum properties on `[FromQuery]`-bound models bind identically to nullable enum action parameters) and is already proven working by `GetPackagesRequest`'s analogous nullable/optional properties. No special handling needed; call out in acceptance testing (FR-3) to confirm with an integration test hitting `?status=Failed`. |

## Specification Amendments
None required to the functional requirements — the spec (FR-1 through FR-4) already correctly anticipated the `ValidationResultBehavior` approach and file layout confirmed here. One addition: the spec's FR-4 acceptance criteria should be read as *requiring* (not merely suggesting) the handler-test updates identified in Risks above; the planner must create an explicit task for this rather than leaving it implicit in "update... if any target the clamping behavior" (they do — this review found them).

## Prerequisites
None. No migrations, no new configuration, no new infrastructure. `FluentValidation` and the `ValidationResultBehavior` shared behavior already exist and are already referenced by `Anela.Heblo.Application`. Implementation can start immediately.
