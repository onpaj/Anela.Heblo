I have everything I need to write the architecture review. The key finding from exploration: `IArticleRepository` lives in the Domain layer (`Anela.Heblo.Domain`), and the established pattern (e.g., `ArticleFeedbackStats`) puts auxiliary repository return types — including `record`s — in the Domain layer next to the interface. The spec's proposed Application-layer location for `ArticleFeedbackProjection` would create a layering inversion (Domain referencing Application).

```markdown
# Architecture Review: Project ArticleRepository.GetFeedbackPagedAsync to DTO at the Database Level

## Skip Design: true

## Architectural Fit Assessment

The change aligns cleanly with the repository's existing shape. `IArticleRepository` already exposes a non-entity return type for an aggregate-style query (`ArticleFeedbackStats`, a `record` in the Domain layer). Introducing a second narrow return type for `GetFeedbackPagedAsync` is a natural continuation of the same pattern, not a new architectural concept.

The two integration points are:
1. **Domain ⇄ Persistence contract**: `IArticleRepository.GetFeedbackPagedAsync` signature changes; only the EF Core implementation is affected.
2. **Persistence ⇄ Application boundary**: `GetArticleFeedbackListHandler` consumes the new projection type and maps it to the OpenAPI-exposed `ArticleFeedbackSummary`. The public DTO is unchanged, so the OpenAPI/TypeScript client is unaffected.

The change is internal-only, surgical, and respects module isolation. `IArticleRepository` has no external consumers beyond this single handler (verified by grep — only the interface, implementation, handler, and one test file reference `GetFeedbackPagedAsync`).

## Proposed Architecture

### Component Overview

```
[Application]
  GetArticleFeedbackListHandler
        │ depends on
        ▼
[Domain]  ◄──── IArticleRepository
                  │ returns
                  ▼
                ArticleFeedbackProjection  ◄── NEW (Domain layer)
                ArticleFeedbackStats           (existing pattern)
                  ▲
                  │ implements
[Persistence]   ArticleRepository
                  │ uses EF Core .Select()
                  ▼
                [ApplicationDbContext.Articles]
```

The projection type is a Domain-layer construct that crosses the Persistence-Application boundary unchanged, the same way `ArticleFeedbackStats` already does. Persistence projects EF entities into it; Application consumes it and maps to the public DTO.

### Key Design Decisions

#### Decision 1: Projection type lives in Domain, not Application

**Options considered:**
- (A) `Anela.Heblo.Application/Features/Article/UseCases/GetFeedbackList/ArticleFeedbackProjection.cs` (spec proposal).
- (B) `Anela.Heblo.Domain/Features/Article/ArticleFeedbackProjection.cs` (next to `IArticleRepository`).
- (C) `Anela.Heblo.Persistence/Features/Article/ArticleFeedbackProjection.cs` (internal to Persistence; handler reshapes via a wrapper).

**Chosen approach:** (B) — Domain layer, next to `IArticleRepository`.

**Rationale:** Option (A) is **architecturally invalid**. `IArticleRepository` is declared in `Anela.Heblo.Domain`, which only references `Anela.Heblo.Xcc`. If the projection lives in Application, the Domain `csproj` would need a reference to Application — a layering inversion that breaks Clean Architecture and would not compile without restructuring project references. Option (C) duplicates the type or forces the handler to re-project, defeating the purpose. Option (B) matches the established convention (`ArticleFeedbackStats` is a `record` in the same Domain folder), preserves layering, and lets both Persistence and Application consume the type without any new project references.

#### Decision 2: `record` over `class` for the projection

**Options considered:** `record` with positional constructor vs. mutable `class`.

**Chosen approach:** `sealed record` with a positional primary constructor (`ArticleFeedbackProjection(Guid Id, string? Title, ...)`).

**Rationale:** The projection never crosses the OpenAPI boundary (verified — `ArticleFeedbackSummary` remains the API contract). The project rule "DTOs are classes, never C# records" exists to protect the OpenAPI generator from positional-parameter mishandling; it does not apply to internal Domain types. The existing `ArticleFeedbackStats` is itself a `sealed record` in the same folder, set the precedent. EF Core 8 translates `.Select(a => new ArticleFeedbackProjection(...))` into a column-restricted `SELECT` provided the constructor parameters bind unambiguously to entity properties.

#### Decision 3: Sort and filter operate on `IQueryable<Article>`; projection happens last

**Options considered:** Project early (after filters, before sort) vs. project late (immediately before `ToListAsync`).

**Chosen approach:** Project late — apply `.Select(...)` after `Skip/Take` and immediately before `ToListAsync`. The `CountAsync` runs against the pre-`Skip/Take` `IQueryable<Article>`.

**Rationale:** Filters reference columns (`PrecisionScore`, `StyleScore`, `RequestedBy`) that are all in the projection, but the sort switch could theoretically be extended to columns not in the projection (e.g., a future `Status` sort). Projecting late keeps the filter/sort logic identical to today's code, reducing the diff and the regression surface. EF Core will still emit a column-restricted `SELECT` because the final `Select` determines the materialised columns — `CountAsync` translates to `SELECT COUNT(*)` and never materialises rows.

#### Decision 4: SQL-shape regression test via `ToQueryString()`

**Options considered:** (a) `IQueryable.ToQueryString()` assertion in an in-memory test; (b) EF Core `DbCommandInterceptor` against a real provider; (c) Snapshot-test the generated SQL.

**Chosen approach:** (a) — a unit-level test against the existing in-memory or SQLite test infrastructure that calls the same `_context.Articles` query path used by `ArticleRepository.GetFeedbackPagedAsync`, captures `query.ToQueryString()`, and asserts the resulting SQL does not contain `HtmlContent` (case-insensitive substring check).

**Rationale:** Simplest, no test-infrastructure changes, no provider-specific behaviour. Refactor the repository so the query-building portion is reachable from a test (either by extracting the `IQueryable` composition into a static/internal helper, or by exposing the configured `IQueryable` through an `internal` test-only seam guarded by `InternalsVisibleTo`). Option (b) is overkill for a single-method guard.

## Implementation Guidance

### Directory / Module Structure

New file:
```
backend/src/Anela.Heblo.Domain/Features/Article/ArticleFeedbackProjection.cs
```

Modified files:
```
backend/src/Anela.Heblo.Domain/Features/Article/IArticleRepository.cs
backend/src/Anela.Heblo.Persistence/Features/Article/ArticleRepository.cs
backend/src/Anela.Heblo.Application/Features/Article/UseCases/GetFeedbackList/GetArticleFeedbackListHandler.cs
backend/test/Anela.Heblo.Tests/Article/UseCases/GetArticleFeedbackListHandlerTests.cs
```

New test (or new fact in an existing repository test file):
```
backend/test/Anela.Heblo.Tests/Article/.../ArticleRepositoryFeedbackProjectionSqlTests.cs
```

No new projects, no `csproj` changes, no DI registration changes.

### Interfaces and Contracts

**`ArticleFeedbackProjection.cs` (Domain layer, new):**
```csharp
namespace Anela.Heblo.Domain.Features.Article;

public sealed record ArticleFeedbackProjection(
    Guid Id,
    string? Title,
    string Topic,
    string? RequestedBy,
    DateTimeOffset CreatedAt,
    int? PrecisionScore,
    int? StyleScore,
    string? FeedbackComment);
```

Field types are taken directly from `Article.cs`:
- `Id` → `Guid` (non-null)
- `Title` → `string?`
- `Topic` → `string` (non-null, defaults to `""`)
- `RequestedBy` → `string?`
- `CreatedAt` → `DateTimeOffset` (note: spec said `DateTime`; the entity uses `DateTimeOffset`)
- `PrecisionScore` → `int?`
- `StyleScore` → `int?`
- `FeedbackComment` → `string?`

**Updated `IArticleRepository.GetFeedbackPagedAsync`:**
```csharp
Task<(IReadOnlyList<ArticleFeedbackProjection> Items, int TotalCount)> GetFeedbackPagedAsync(
    bool? hasFeedback,
    string? requestedBy,
    string sortBy,
    bool descending,
    int page,
    int pageSize,
    CancellationToken ct = default);
```

All other methods on `IArticleRepository` remain untouched — `GetByIdAsync`, `GetForUpdateAsync`, `GetPagedAsync`, `GetWithStepsAsync` legitimately need the full `Article` aggregate (sources, steps) and are out of scope.

**Updated `ArticleRepository.GetFeedbackPagedAsync` body (preserving existing filter/sort logic):**
```csharp
var query = _context.Articles.AsNoTracking();

if (hasFeedback == true)
    query = query.Where(a => a.PrecisionScore != null || a.StyleScore != null);
else if (hasFeedback == false)
    query = query.Where(a => a.PrecisionScore == null && a.StyleScore == null);

if (!string.IsNullOrWhiteSpace(requestedBy))
    query = query.Where(a => a.RequestedBy == requestedBy);

query = sortBy switch
{
    "PrecisionScore" => descending ? query.OrderByDescending(a => a.PrecisionScore) : query.OrderBy(a => a.PrecisionScore),
    "StyleScore"     => descending ? query.OrderByDescending(a => a.StyleScore)     : query.OrderBy(a => a.StyleScore),
    _                => descending ? query.OrderByDescending(a => a.CreatedAt)      : query.OrderBy(a => a.CreatedAt),
};

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

**Updated `GetArticleFeedbackListHandler` mapping** (only the projection lambda changes; logic is byte-identical):
```csharp
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

### Data Flow

For a feedback-list page request:
1. Controller → MediatR `Send` → `GetArticleFeedbackListHandler.Handle`.
2. Handler issues two parallel repository calls: `GetFeedbackPagedAsync` (now returns `IReadOnlyList<ArticleFeedbackProjection>`) and `GetFeedbackStatsAsync` (unchanged).
3. `ArticleRepository.GetFeedbackPagedAsync` builds the `IQueryable<Article>` exactly as today (filters, sort, count), then projects to `ArticleFeedbackProjection` via `.Select(...)` before `ToListAsync`. SQL `SELECT` lists only the eight projected columns.
4. Handler maps the projection rows to the OpenAPI-exposed `ArticleFeedbackSummary` (computing `HasComment` from `FeedbackComment`) and returns the response.

OpenAPI contract, controller, frontend hook, TypeScript client: all unchanged.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Projection placed in Application layer per spec text → Domain→Application reference cycle, build fails. | High | Place `ArticleFeedbackProjection.cs` in `Anela.Heblo.Domain/Features/Article/` (this review's Decision 1). Verify with `dotnet build` after adding the file. |
| EF Core translation surprise: `.Select(new Record(...))` falls back to client-side evaluation, defeating the optimisation. | Medium | Add the SQL-shape regression test (Decision 4) that asserts `HtmlContent` is absent from `query.ToQueryString()`. CI fails if EF Core ever changes behaviour. |
| Existing handler unit tests (`GetArticleFeedbackListHandlerTests`) break because mocks return `IReadOnlyList<Article>`. | Low | Spec FR-4 already calls this out — update mock setups to return `IReadOnlyList<ArticleFeedbackProjection>`. The three existing tests need only their `ReturnsAsync(...)` lines adjusted. |
| Future caller of `GetFeedbackPagedAsync` needs a field not in the projection (e.g., `Status`, `GeneratedAt`). | Low | Document in `ArticleFeedbackProjection` that it serves the feedback-list use case; future callers needing more should add a separate repository method (e.g., `GetFeedbackPagedWithStatusAsync`) rather than widening this projection. |
| Sort by a column omitted from the projection — currently impossible (`AllowedSortColumns` enumerates `CreatedAt`, `PrecisionScore`, `StyleScore`, all in the projection), but adding `Status` sort later would require including the column. | Low | No action now. If `AllowedSortColumns` grows, the implementer must add the new column to `ArticleFeedbackProjection` in the same change. Note this in a one-line comment above the `Select(...)`. |
| `record` constructor parameter order drift later renames or reorders fields → silent positional mis-binding in EF projection. | Low | Constructor parameters bind by name in EF Core 8 record-projection translation, but positional callers in the repository will fail at compile time on any signature change — this is a feature, not a risk. |

## Specification Amendments

1. **FR-2 location amendment (mandatory).** The spec proposes `backend/src/Anela.Heblo.Application/Features/Article/UseCases/GetFeedbackList/ArticleFeedbackProjection.cs`. This is incorrect because `IArticleRepository` lives in `Anela.Heblo.Domain`, which does not (and must not) reference `Anela.Heblo.Application`. Correct location: `backend/src/Anela.Heblo.Domain/Features/Article/ArticleFeedbackProjection.cs`, next to `IArticleRepository.cs` and `ArticleFeedbackStats.cs`.

2. **FR-2 / OQ-1 confirmation.** The spec's Open Question OQ-1 (project-wide "DTOs are classes, never records" rule) is resolved: the rule does not apply to internal projection types. Precedent: `ArticleFeedbackStats` (Domain, `sealed record`). Use `sealed record` for `ArticleFeedbackProjection`. (The spec already states OQ-1 is "None" in its final form; this is a confirmation, not a change.)

3. **Data Model `CreatedAt` type clarification.** The spec writes `CreatedAt : DateTime (or DateTimeOffset, matching entity)`. The entity is `DateTimeOffset` (`Article.CreatedAt`). The projection MUST use `DateTimeOffset` to avoid a redundant value-conversion in EF Core and to keep the mapping to `ArticleFeedbackSummary.CreatedAt` (also `DateTimeOffset`) trivially type-compatible.

4. **NFR-4 / FR-1 test approach narrowed.** The spec lists three acceptable mechanisms. To keep test infrastructure simple and avoid adding an EF interceptor fixture, **prefer mechanism (a)**: capture `query.ToQueryString()` on the pre-`ToListAsync` `IQueryable<ArticleFeedbackProjection>` and assert `HtmlContent` is absent from the SQL string. This requires exposing the query-composition path to a test — either via `InternalsVisibleTo` on a small internal helper, or by adding an integration test that uses SQLite/InMemory and calls the public repository method against an instrumented `DbContext`. Choose whichever path requires fewer changes to existing test infrastructure.

5. **Acceptance criterion clarification for FR-1.** Add: the SQL-shape test MUST also assert that the projected columns (`Id`, `Title`, `Topic`, `RequestedBy`, `CreatedAt`, `PrecisionScore`, `StyleScore`, `FeedbackComment`) appear in the `SELECT`. A pure "doesn't contain HtmlContent" assertion would pass on a query that selects nothing at all.

## Prerequisites

None. The change requires:
- No database migrations (schema unchanged).
- No new NuGet packages.
- No new project references.
- No DI registration changes.
- No infrastructure changes.
- No feature flag.
- No OpenAPI/TypeScript client regeneration.

Implementation may begin immediately. Validate with `dotnet build` after adding `ArticleFeedbackProjection.cs` and updating the interface, and with `dotnet test` after updating the handler-test mocks.
```