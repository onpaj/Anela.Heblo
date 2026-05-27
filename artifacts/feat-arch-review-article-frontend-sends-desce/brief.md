## Module
Article

## Finding
`ArticlesController.cs:88` declares the feedback-list sort-direction parameter as:

```csharp
[FromQuery] bool sortDescending = true,
```

ASP.NET Core model binding maps this to the query string key `sortDescending`.

`useArticles.ts:267-268` appends a different key:

```typescript
if (params.descending !== undefined)
    searchParams.append('descending', params.descending.toString());
```

The frontend sends `?descending=false`; the backend never sees the `sortDescending` key, so it always uses the default value (`true` / descending). Any sort-direction toggle in the feedback list UI is silently ignored.

## Why it matters
- The feedback list always renders in descending order regardless of what the user selects.
- The bug is invisible at runtime (no error, no warning), making it easy to miss in testing.
- `sortBy` and `hasFeedback` are spelled correctly in both sides; `sortDescending` is the only mismatch.

## Suggested fix
Align one side with the other. The controller parameter name is the contract — the simplest fix is to update the frontend:

```typescript
// useArticles.ts:268
searchParams.append('sortDescending', params.descending.toString());
```

Alternatively rename the `ArticleFeedbackListParams` field from `descending` to `sortDescending` for consistency.

---
_Filed by daily arch-review routine on 2026-05-25._