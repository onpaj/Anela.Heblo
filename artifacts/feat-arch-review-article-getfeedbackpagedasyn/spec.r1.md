# Specification: Project ArticleRepository.GetFeedbackPagedAsync to DTO at the Database Level

## Summary
Replace the entity-materialising query in `ArticleRepository.GetFeedbackPagedAsync` with a database-level projection that returns only the eight columns the feedback list handler needs. This eliminates fetching the large `HtmlContent` text column on every feedback-list page load, reducing wire bytes and memory allocations by roughly an order of magnitude per request.

## Background
The Article module exposes a feedback list page that lets reviewers browse articles awaiting feedback. The handler powering this page (`GetArticleFeedbackListHandler`) consumes a paged result from `ArticleRepository.GetFeedbackPagedAsync` and projects each entity into an `ArticleFeedbackSummary` DTO with only seven scalar fields plus a `HasComment` boolean derived from `FeedbackComment`.

The repository currently materialises full `Article` entities (`ToListAsync` over `IQueryable<Article>` with no `.Select`). The `Article.HtmlContent` column is declared as `text` in `ArticleConfiguration` with no length cap and stores the full generated article body — typically 5–20 KB of HTML per row. For a 50-item page, this is ~500 KB of dead transfer per request, allocated, copied across the network, deserialised by EF Core, then discarded when the handler projects to its DTO.

The arch-review routine flagged this on 2026-05-27 as a KISS violation: the repository returns more than its only caller needs, and any future caller of `GetFeedbackPagedAsync` inherits the same overhead. The fix is straightforward: project at the database in the repository, change the method's return type to a purpose-built projection record, and update the handler's mapping accordingly.

## Functional Requirements

### FR-1: Database-level projection in GetFeedbackPagedAsync
`ArticleRepository.GetFeedbackPagedAsync` MUST issue a SQL query that selects only the columns required by `GetArticleFeedbackListHandler`. The `HtmlContent` column MUST NOT appear in the generated SQL `SELECT` list.

The projected columns are:
- `Id`
- `Title`
- `Topic`
- `RequestedBy`
- `CreatedAt`
- `PrecisionScore`
- `StyleScore`
- `FeedbackComment` (used to compute `HasComment` in the handler; not exposed downstream)

**Acceptance criteria:**
- An integration test (or in-memory DB test) capturing the EF Core-generated SQL for `GetFeedbackPagedAsync` confirms the `SELECT` clause contains only the eight listed columns and excludes `HtmlContent`.
- The method's behavioural contract (paging, filtering, sorting, total count semantics) is unchanged — existing handler tests continue to pass without modification beyond the type change.

### FR-2: Introduce ArticleFeedbackProjection type
A new type `ArticleFeedbackProjection` MUST be introduced to carry the projected row shape between the repository and the handler.

- Location: `backend/src/Anela.Heblo.Application/Features/Article/UseCases/GetFeedbackList/ArticleFeedbackProjection.cs` (Application layer, alongside the handler that consumes it).
- Shape: an immutable C# `record` (this is an **internal projection type**, not a DTO crossing the OpenAPI boundary, so the project-wide "DTOs are classes, never records" rule does not apply — see Open Questions OQ-1 for confirmation request).
- Fields, in declaration order: `Id`, `Title`, `Topic`, `RequestedBy`, `CreatedAt`, `PrecisionScore`, `StyleScore`, `FeedbackComment`. Types match the corresponding `Article` entity properties (including nullability).
- Visibility: `public` (it is referenced from both the Persistence and Application projects via `IArticleRepository`).

**Acceptance criteria:**
- The type compiles and is referenced by both `IArticleRepository.GetFeedbackPagedAsync` and `GetArticleFeedbackListHandler`.
- No other type in the solution duplicates this shape; there is exactly one projection record for the feedback list path.

### FR-3: Update IArticleRepository contract
The `IArticleRepository.GetFeedbackPagedAsync` signature MUST change to return `Task<(IReadOnlyList<ArticleFeedbackProjection> Items, int TotalCount)>` instead of `Task<(IReadOnlyList<Article> Items, int TotalCount)>`. All other parameters (filters, paging, sort, cancellation token) remain identical.

**Acceptance criteria:**
- `IArticleRepository` interface reflects the new return type.
- `ArticleRepository` implementation matches the interface.
- No call site of `GetFeedbackPagedAsync` exists outside `GetArticleFeedbackListHandler` (verified by repository-wide grep before merge); if any other caller surfaces, FR-5 applies.

### FR-4: Simplify handler mapping
`GetArticleFeedbackListHandler` MUST be updated so its mapping from repository result to `ArticleFeedbackSummary` consumes `ArticleFeedbackProjection` instances directly. The mapping logic for `HasComment` (computed from `FeedbackComment`) is preserved.

**Acceptance criteria:**
- The handler compiles against the new repository signature.
- All existing unit/integration tests for `GetArticleFeedbackListHandler` pass without changes to their assertions (only any test-side mocks/fakes of `IArticleRepository` need their return type adjusted).
- The shape and contents of `ArticleFeedbackSummary` (the OpenAPI-exposed DTO) are unchanged. The OpenAPI client does **not** regenerate as a result of this change.

### FR-5: Preserve other repository methods returning full Article
This change is scoped strictly to `GetFeedbackPagedAsync`. Other `ArticleRepository` methods that legitimately return full `Article` entities (e.g. detail/admin queries) MUST remain untouched. The all-or-nothing concern raised in the brief is resolved by giving the feedback list path its own narrow projection, not by changing the repository's other contracts.

**Acceptance criteria:**
- `git diff` for the change touches only: `ArticleRepository.cs`, `IArticleRepository.cs`, the new `ArticleFeedbackProjection.cs`, `GetArticleFeedbackListHandler.cs`, and the corresponding test files. No other repository methods are modified.

## Non-Functional Requirements

### NFR-1: Performance
- The SQL generated for `GetFeedbackPagedAsync` MUST exclude the `HtmlContent` column. Verified at the EF Core query-translation level (FR-1 acceptance criterion).
- Expected wire-byte reduction per feedback-list page: ~90% (from ~500 KB to ~50 KB for a 50-item page with typical content sizes).
- Existing pagination performance (filtering, sorting, count query) MUST NOT regress. The same indexes and predicates apply.

### NFR-2: Security
No security-relevant change. The set of fields exposed to the API consumer via `ArticleFeedbackSummary` is unchanged. Removing `HtmlContent` from the transport path is a defence-in-depth bonus (less sensitive content traverses unnecessary layers) but is not the goal.

### NFR-3: Maintainability
- The repository contract narrows to match its caller's needs (KISS). Any future caller that genuinely needs full `Article` entities must use (or add) a different method.
- The projection record lives next to its consumer handler, keeping cohesion high.

### NFR-4: Test coverage
- Existing test coverage for `GetArticleFeedbackListHandler` MUST be preserved.
- At least one integration test MUST assert that the SQL generated for `GetFeedbackPagedAsync` does not reference `HtmlContent`. Acceptable implementations: (a) inspecting `IQueryable.ToQueryString()` on the pre-`ToListAsync` query, (b) using an EF Core interceptor in a test fixture to capture the executed SQL, or (c) any equivalent mechanism that fails if `HtmlContent` is re-introduced into the projection.

## Data Model

No schema changes. The `Article` entity, `ArticleConfiguration`, and database table are untouched.

New in-memory type:

```
ArticleFeedbackProjection (record, Application layer)
├── Id              : <Article.Id type>
├── Title           : string
├── Topic           : string
├── RequestedBy     : string
├── CreatedAt       : DateTime (or DateTimeOffset, matching entity)
├── PrecisionScore  : <score type, likely int? or decimal?>
├── StyleScore      : <score type>
└── FeedbackComment : string?
```

Exact field types mirror the `Article` entity properties as declared in `ArticleConfiguration` / the domain model. The implementer reads them from the existing entity rather than re-deriving.

## API / Interface Design

**No public API changes.** The `ArticleFeedbackSummary` DTO returned by the feedback list endpoint is unchanged, so the OpenAPI contract and the generated TypeScript client are unaffected.

**Internal interface change:**

```csharp
// IArticleRepository.cs — before
Task<(IReadOnlyList<Article> Items, int TotalCount)> GetFeedbackPagedAsync(
    /* existing parameters */,
    CancellationToken ct);

// IArticleRepository.cs — after
Task<(IReadOnlyList<ArticleFeedbackProjection> Items, int TotalCount)> GetFeedbackPagedAsync(
    /* existing parameters, unchanged */,
    CancellationToken ct);
```

**Repository implementation shape:**

```csharp
var query = _context.Articles.AsNoTracking();
// existing filter / sort logic, applied to query (still typed as IQueryable<Article>)
var total = await query.CountAsync(ct);
var items = await query
    .Skip((page - 1) * pageSize)
    .Take(pageSize)
    .Select(a => new ArticleFeedbackProjection(
        a.Id, a.Title, a.Topic, a.RequestedBy,
        a.CreatedAt, a.PrecisionScore, a.StyleScore, a.FeedbackComment))
    .ToListAsync(ct);
return (items, total);
```

Filtering and sorting continue to operate on `IQueryable<Article>` so that existing predicates (which may reference any column) remain valid. The projection happens immediately before `ToListAsync`.

## Dependencies

- **EF Core**: relies on existing translation of `.Select(a => new ArticleFeedbackProjection(...))` into a column-restricted `SELECT`. EF Core 8 supports projecting into records via constructor.
- **No new NuGet packages.**
- **No new external services.**
- **Migration**: none — schema unchanged.
- **Feature flag**: none — this is a pure internal refactor with identical observable behaviour at the API boundary.

## Out of Scope

- Projecting other `IArticleRepository` methods that return full `Article` entities. Each such method has its own callers and call patterns; revisit them as separate findings if they show the same anti-pattern.
- Adding `HasMaxLength` or any other column-level constraints to `HtmlContent` in `ArticleConfiguration`.
- Changing pagination defaults, max page size, or sort options.
- Changing the `ArticleFeedbackSummary` DTO shape or the feedback list UI.
- Adding caching for the feedback list endpoint.
- Compressing `HtmlContent` at rest.

## Open Questions

None.

## Status: COMPLETE