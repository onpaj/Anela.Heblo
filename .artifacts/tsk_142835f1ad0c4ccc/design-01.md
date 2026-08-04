# Design: Cap `pageSize` in `ListArticlesHandler`

No UI is involved — this is a backend-only handler fix. `ArticlesController.List`'s route, query
parameters, and response contract are unchanged; only the internal value handling changes. The UX/UI
section is omitted per the plan's scope (no controller signature or OpenAPI contract change).

## Component design

### `ListArticlesHandler` (modified)

**Responsibility (updated):** translate a `ListArticlesRequest` into a bounded, valid page-fetch call
against `IArticleRepository`, then map the result to `ListArticlesResponse`. The handler becomes the
single point of truth for *what pagination values are actually used*, independent of what the caller
requested — matching the responsibility `GetArticleFeedbackListHandler` already has for its own request.

**Collaborators:** unchanged — `IArticleRepository` is the only dependency (constructor signature does
not change).

**New internal behavior:**
- Compute `page = Math.Max(1, request.Page)`.
- Compute `pageSize = Math.Clamp(request.PageSize, 1, 100)`.
- Pass `page`/`pageSize` (the clamped locals), not `request.Page`/`request.PageSize`, to
  `_repository.GetPagedAsync`.
- Populate `ListArticlesResponse.Page`/`PageSize` from the same clamped locals, so the echoed values
  always match what was actually fetched (`TotalCount`/`TotalPages` stay internally consistent).

This mirrors `GetArticleFeedbackListHandler.Handle` (`GetFeedbackList/GetArticleFeedbackListHandler.cs:28-29`)
line-for-line for the `page` clamp, and uses `Math.Clamp` instead of an allow-list for `pageSize` since
`ListArticles` has no fixed-tier UI page-size selector — any value 1–100 is legitimate, unlike the
feedback list's `{10, 20, 50}` dropdown.

No other component changes. `ArticlesController`, `ListArticlesRequest`, `IArticleRepository`, and
`ArticleRepository` are untouched, per the plan's explicit out-of-scope list.

## Data schemas

### Request — `ListArticlesRequest` (unchanged shape)

```csharp
public class ListArticlesRequest : IRequest<ListArticlesResponse>
{
    public ArticleStatus? Status { get; set; }

    [Range(1, int.MaxValue)]
    public int Page { get; set; } = 1;

    [Range(1, 100)]
    public int PageSize { get; set; } = 20;
}
```

No field added/removed/retyped. The `[Range]` attributes remain in place as documentation of intent
(per the plan's default resolution to the open question) even though they are not evaluated for this
endpoint today — the handler-side clamp is now the actual enforcement mechanism.

### Response — `ListArticlesResponse` (shape unchanged, **value semantics changed**)

```csharp
public sealed class ListArticlesResponse : BaseResponse
{
    public List<ArticleListItemDto> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}
```

No field added/removed/retyped — the JSON schema and generated OpenAPI/TypeScript client are byte-for-byte
identical. What changes is **which values populate `Page`/`PageSize`**:

| Request                                | Today (before fix)      | After fix                          |
|-----------------------------------------|--------------------------|-------------------------------------|
| `pageSize=1000000`                      | echoes `1000000`         | repository called with `100`; response echoes `100` |
| `pageSize=0` / negative                 | echoes raw value         | repository called with `1`; response echoes `1` |
| `page=0` / negative                     | echoes raw value         | repository called with `1`; response echoes `1` |
| `pageSize=10/20/50/100` (in range)      | echoes raw value         | unchanged — passes through as-is |

This is the one observable behavior change and must be called out in the PR description, as the plan
notes.

### Repository call — `IArticleRepository.GetPagedAsync` (signature unchanged)

```csharp
Task<(IReadOnlyList<Article> Items, int TotalCount)> GetPagedAsync(
    ArticleStatus? status,
    int page,
    int pageSize,
    CancellationToken cancellationToken);
```

No signature change. Only the *arguments* passed by the handler change (clamped locals instead of raw
request fields).

### No event payloads or DB schema changes

This fix touches in-memory pagination arithmetic only; no persisted entity, migration, or integration
event is affected.

## Interactions

```
Client → ArticlesController.List(status, page, pageSize)
       → new ListArticlesRequest { Status, Page = page, PageSize = pageSize }
       → MediatR.Send(request)
       → ListArticlesHandler.Handle(request, ct)
             page     = Math.Max(1, request.Page)
             pageSize = Math.Clamp(request.PageSize, 1, 100)
             (items, totalCount) = repository.GetPagedAsync(request.Status, page, pageSize, ct)
             return ListArticlesResponse { Items=..., TotalCount, Page=page, PageSize=pageSize }
       ← ListArticlesResponse
```

Test doubles for `ListArticlesHandlerTests` continue to mock `IArticleRepository.GetPagedAsync` and
assert on the arguments the handler passes it (already the pattern used by
`Handle_PassesStatusFilterThroughToRepository`), plus assert on the response's echoed `Page`/`PageSize`
for out-of-range inputs (already the pattern used by `Handle_ReturnsMappedListWithPaginationInfo`).
