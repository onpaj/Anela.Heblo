# Design: Article paging validation at the API boundary

## Component Design

### `ListArticlesRequestValidator` (new)
- **Location:** `Application/Features/Article/UseCases/ListArticles/ListArticlesRequestValidator.cs`
- **Responsibility:** Sole owner of the validity rules for `ListArticlesRequest`. Runs inside the MediatR pipeline, before `ListArticlesHandler` is invoked.
- **Contract:** `AbstractValidator<ListArticlesRequest>` (FluentValidation).
  - `Page`: `>= 1`.
  - `PageSize`: in `[1, 100]` inclusive.
- **Not responsible for:** `Status` (no constraint — it's an optional filter, any valid `ArticleStatus?` or `null` is acceptable; enum binding failures are already handled by model binding, not this validator).

### `GetArticleFeedbackListRequestValidator` (new)
- **Location:** `Application/Features/Article/UseCases/GetFeedbackList/GetArticleFeedbackListRequestValidator.cs`
- **Responsibility:** Sole owner of the validity rules for `GetArticleFeedbackListRequest`, replacing the allowlist logic currently embedded in the handler.
- **Contract:** `AbstractValidator<GetArticleFeedbackListRequest>`.
  - `Page`: `>= 1`.
  - `PageSize`: must be one of `{10, 20, 50}` (the two allowlists — page sizes and sort columns — are declared once, as `private static readonly` arrays on the validator itself, since they are validation constants; the handler no longer needs to know them).
  - `SortBy`: must be one of `{"CreatedAt", "PrecisionScore", "StyleScore"}`.
- **Not responsible for:** `HasFeedback`, `RequestedBy`, `SortDescending` — unconstrained, pass through as-is (a `null`/absent `RequestedBy` or `HasFeedback` is a valid "no filter" state; `SortDescending` is a plain `bool`, always valid).

### `ArticlesController` (modified)
- **Responsibility unchanged**: translate HTTP requests to MediatR requests and MediatR responses to HTTP responses via `HandleResponse`.
- **Change**: `List` and `FeedbackList` actions bind their MediatR request type directly via `[FromQuery]` instead of assembling it field-by-field from separate `[FromQuery]` scalar parameters. This is the mechanism change that lets the pipeline-level validators (and, if ever needed, native ASP.NET Core `[ApiController]` validation) actually see a `ModelState`/request instance to validate — today the manually-constructed request bypasses that entirely.
- **Query parameter surface is unchanged** — same names, same optionality, same defaults (defaults now come from the request type's property initializers, e.g. `public int Page { get; set; } = 1;`, rather than from the action parameter's `= 1` default; both produce the same observable default of `1`).

### `ListArticlesHandler` (modified)
- **Responsibility narrows to**: given an already-valid `ListArticlesRequest`, fetch the page and map it to a response. No longer responsible for defending against invalid `Page`/`PageSize` — that is a precondition guaranteed by the pipeline before `Handle` is ever called.
- Uses `request.Page` / `request.PageSize` directly wherever `page`/`pageSize` locals were previously read from the clamp.

### `GetArticleFeedbackListHandler` (modified)
- **Responsibility narrows to**: given an already-valid `GetArticleFeedbackListRequest`, fetch the page + stats and map them. No longer responsible for allowlist-checking `PageSize`/`SortBy` or defaulting `Page` — those are preconditions guaranteed by the pipeline.
- Uses `request.Page` / `request.PageSize` / `request.SortBy` directly. The `AllowedPageSizes`/`AllowedSortColumns` static arrays are deleted from the handler (their single remaining owner is the validator).

### `ArticleModule` (modified)
- **Responsibility**: composition root for the Article feature's DI registrations. Gains four registrations (two `IValidator<TRequest>`, two `IPipelineBehavior<TRequest,TResponse>` closed to `ValidationResultBehavior<TRequest,TResponse>`), following the exact shape already used in `PackagingModule`/`AnalyticsModule`/`FileStorageModule`. No new abstraction is introduced — this module only wires existing, shared infrastructure (`ValidationResultBehavior<,>`) to two new validators.

## Data Schemas

### Request shapes (unchanged wire format, now validated before use)

`GET /api/articles`
| Query param | Type | Default | Validity constraint (new) |
|---|---|---|---|
| `status` | `ArticleStatus?` | `null` | none (unconstrained optional filter) |
| `page` | `int` | `1` | `>= 1` |
| `pageSize` | `int` | `20` | `1..100` inclusive |

`GET /api/articles/feedback/list`
| Query param | Type | Default | Validity constraint (new) |
|---|---|---|---|
| `hasFeedback` | `bool?` | `null` | none |
| `requestedBy` | `string?` | `null` | none |
| `sortBy` | `string` | `"CreatedAt"` | one of `CreatedAt`, `PrecisionScore`, `StyleScore` |
| `sortDescending` | `bool` | `true` | none |
| `page` | `int` | `1` | `>= 1` |
| `pageSize` | `int` | `20` | one of `10`, `20`, `50` |

### Response shapes on validation failure (new observable behavior)

Both endpoints, on a validation failure, now return the same envelope every other Article error uses — `BaseResponse`-derived, `Success = false`, `ErrorCode` set — instead of a 200 with silently-corrected values:

```jsonc
// ListArticlesResponse on invalid input, e.g. GET /api/articles?pageSize=0
{
  "success": false,
  "errorCode": "ValidationError",
  "params": { /* FluentValidation CustomState, if any was set — none configured here, so likely null/omitted */ },
  "items": [],
  "totalCount": 0,
  "page": 0,
  "pageSize": 0
}
```
```jsonc
// GetArticleFeedbackListResponse on invalid input, e.g. GET /api/articles/feedback/list?pageSize=30
{
  "success": false,
  "errorCode": "ValidationError",
  "items": [],
  "stats": { "totalArticles": 0, "totalWithFeedback": 0, "avgPrecisionScore": null, "avgStyleScore": null },
  "totalCount": 0,
  "page": 0,
  "pageSize": 0
}
```
The HTTP status code for both is whatever `HttpStatusCodeAttribute` declares for `ErrorCodes.ValidationError` (already resolved centrally by `BaseApiController.GetStatusCodeForError` — no new mapping needed).

### Response shapes on success (unchanged)
No fields, types, or defaults change on the success path. `Page`/`PageSize` echoed back on both responses are exactly the caller's validated input, same as they were exactly the caller's clamped input before this change — the only difference is which values are permitted to reach that point.
