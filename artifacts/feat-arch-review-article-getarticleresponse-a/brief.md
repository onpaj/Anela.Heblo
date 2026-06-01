## Module
Article

## Finding

`GetArticleResponse.cs:14` and `ArticleListItemDto.cs:8` both declare `Status` as `string`:

```csharp
// GetArticleResponse.cs:14
public string Status { get; set; } = string.Empty;

// ArticleListItemDto.cs:8
public string Status { get; set; } = string.Empty;
```

Both handlers then call `.ToString()` explicitly to produce the string value:

```csharp
// GetArticleHandler.cs:38
Status = article.Status.ToString(),

// ListArticlesHandler.cs:32
Status = a.Status.ToString(),
```

`GenerateArticleResponse.cs:16` does the opposite and correctly uses the enum type:

```csharp
public ArticleStatus Status { get; set; }
```

Because the global `JsonStringEnumConverter` is configured in `Program.cs:121`, the wire format is identical either way — both produce `"Generated"` etc. But the NSwag OpenAPI export sees the two `string` properties and generates `status?: string` in the TypeScript client for both `GetArticleResponse` and `ArticleListItemDto`, while generating `status?: ArticleStatus` for `GenerateArticleResponse`.

The difference is visible in the generated client (`frontend/src/api/generated/api-client.ts`):
- `GenerateArticleResponse.status?: ArticleStatus` — line 12745 ✅
- `GetArticleResponse.status?: string` — line 12872 ❌
- `ArticleListItemDto.status?: string` — line 13210 ❌

As a result, `useArticles.ts` must unsafely cast to bridge the gap:

```typescript
// useGetArticleQuery, line 162
status: (response.status as ArticleStatus) ?? ArticleStatus.Queued,

// useListArticlesQuery, line 139
status: (item.status as ArticleStatus) ?? ArticleStatus.Queued,
```

## Why it matters

- The `as ArticleStatus` cast is a type lie: TypeScript's compiler accepts it unconditionally and provides no safety if the backend ever changes the serialisation of this field.
- `IN_PROGRESS_STATUSES.has(status)` in the polling logic depends on `status` being the correctly-typed string enum value; any future deviation between the cast and the real wire value would silently break polling with no compile-time signal.
- Inconsistency within the same module: three response DTOs, three different type declarations for the same concept — a reader must check all three to understand the contract.

## Suggested fix

Change both DTOs to use `ArticleStatus` and remove the explicit `.ToString()` calls — the global converter handles serialisation:

```csharp
// GetArticleResponse.cs:14
public ArticleStatus Status { get; set; }

// ArticleListItemDto.cs:8
public ArticleStatus Status { get; set; }
```

Remove the `.ToString()` from both handlers:

```csharp
// GetArticleHandler.cs:38
Status = article.Status,

// ListArticlesHandler.cs:32
Status = a.Status,
```

After regenerating the TypeScript client, remove the `as ArticleStatus` casts in `useArticles.ts` (lines 139 and 162) — the types will be correct without them.

---
_Filed by daily arch-review routine on 2026-05-25._