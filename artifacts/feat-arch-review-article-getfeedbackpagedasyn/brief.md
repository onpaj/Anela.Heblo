## Module
Article

## Finding
`ArticleRepository.GetFeedbackPagedAsync` materialises full `Article` entities — including the `HtmlContent` text column — but the only caller (`GetArticleFeedbackListHandler`) projects to a seven-field DTO:

```csharp
// backend/src/Anela.Heblo.Persistence/Features/Article/ArticleRepository.cs:59-97
public async Task<(IReadOnlyList<DomainArticle> Items, int TotalCount)> GetFeedbackPagedAsync(...)
{
    var query = _context.Articles.AsNoTracking();
    // ... filters and sorting ...
    var items = await query
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .ToListAsync(ct);   // loads ALL columns, including HtmlContent
    return (items, total);
}
```

```csharp
// backend/src/Anela.Heblo.Application/Features/Article/UseCases/GetFeedbackList/GetArticleFeedbackListHandler.cs:44-54
Items = items.Select(a => new ArticleFeedbackSummary
{
    Id = a.Id,
    Title = a.Title,
    Topic = a.Topic,
    RequestedBy = a.RequestedBy,
    CreatedAt = a.CreatedAt,
    PrecisionScore = a.PrecisionScore,
    StyleScore = a.StyleScore,
    HasComment = !string.IsNullOrWhiteSpace(a.FeedbackComment),
}).ToList(),
```

`HtmlContent` is a `text` column (no `HasMaxLength` in `ArticleConfiguration`) containing the full generated article — typically 5–20 KB of HTML per article. This column is fetched from the database, transferred over the wire, and held in memory for every page load of the feedback list, then silently discarded.

The repository method also returns the full entity for integration/admin queries that genuinely need all fields, so the current signature forces an all-or-nothing choice.

## Why it matters
- **Unnecessary I/O and memory**: `HtmlContent` is the largest column on the table. Loading it for every feedback-list page multiplies network bytes and allocations by ~10× vs. projecting to needed fields.
- **Scales poorly**: As the article backlog grows the cost per page load grows linearly. A feedback list showing 50 items at 10 KB per article body = 500 KB of dead transfer per request.
- **KISS principle**: the repository returns more than it contracts with its callers — every future consumer of this method is implicitly burdened with the same overhead.

## Suggested fix
Add a lightweight DB projection directly in the repository, returning only the columns the handler needs. The minimal change is an inline `.Select()` inside `GetFeedbackPagedAsync`:

```csharp
// backend/src/Anela.Heblo.Persistence/Features/Article/ArticleRepository.cs
public async Task<(IReadOnlyList<ArticleFeedbackProjection> Items, int TotalCount)> GetFeedbackPagedAsync(...)
{
    var query = _context.Articles.AsNoTracking();
    // ... same filter/sort logic ...
    var items = await query
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .Select(a => new ArticleFeedbackProjection(
            a.Id, a.Title, a.Topic, a.RequestedBy,
            a.CreatedAt, a.PrecisionScore, a.StyleScore, a.FeedbackComment))
        .ToListAsync(ct);
    return (items, total);
}
```

`ArticleFeedbackProjection` would be a simple record/class in the Application layer (or Persistence if kept internal). The `IArticleRepository` interface signature would change to `(IReadOnlyList<ArticleFeedbackProjection> Items, int TotalCount)`. The handler mapping becomes trivial.

---
_Filed by daily arch-review routine on 2026-05-27._