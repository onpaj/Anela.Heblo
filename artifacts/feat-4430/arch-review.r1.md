# Architecture Review: ArticleRepository feedback query tests

## Skip Design: true
Backend test-only change; no UI.

## Architectural Fit Assessment
Fits existing convention: `backend/test/Anela.Heblo.Tests/Repositories/MeetingTranscriptRepositoryTests.cs` uses `ApplicationDbContext` with `UseInMemoryDatabase(Guid.NewGuid().ToString())`. Existing Article persistence tests live in `backend/test/Anela.Heblo.Tests/Article/Persistence/`.

## Proposed Architecture

### Component Overview
One new xUnit class `ArticleRepositoryFeedbackQueryTests` constructing `ArticleRepository` over an InMemory `ApplicationDbContext`.

### Key Design Decisions
#### Decision 1: InMemory over Sqlite
Sqlite cannot translate ordering on `DateTimeOffset`, which the default sort uses; InMemory evaluates in LINQ-to-objects and handles it. The brief allows either. Chosen: InMemory. Rationale: matches repo convention, no provider quirks. Trade-off: SQL translation is not validated; the existing Postgres integration test covers that for the projection.

## Implementation Guidance
### Directory / Module Structure
Create `backend/test/Anela.Heblo.Tests/Article/Persistence/ArticleRepositoryFeedbackQueryTests.cs`, namespace `Anela.Heblo.Tests.Article.Persistence`. Beware the namespace `Anela.Heblo.Tests.Article` shadows the `Article` type: use alias `using DomainArticle = Anela.Heblo.Domain.Features.Article.Article;`.

### Interfaces and Contracts
Call repository public methods only. Seed through `_context.Articles.Add` + `SaveChangesAsync`.

### Data Flow
Seed -> call repository -> assert on returned `Items`/`TotalCount`/stats.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Null ordering differs between InMemory and Postgres | Low | Sort tests seed non-null scores for the ordering assertions |
| `GroupBy(_ => 1)` unsupported in InMemory | Low | Verify by running; if unsupported, report and fall back to Sqlite for stats only |

## Specification Amendments
None.

## Prerequisites
None.
