# Specification: ArticleRepository feedback query unit tests

## Summary
Add unit tests for `ArticleRepository.GetFeedbackPagedAsync` and `GetFeedbackStatsAsync`, which are largely uncovered (23% line coverage). Test-only; no production code changes.

## Background
Issue #4430 (coverage-gap routine). Swapping OR/AND in the tri-state `hasFeedback` filter would silently invert the feedback review page results. The only existing test (`ArticleRepositoryFeedbackProjectionSqlTests`) is a Testcontainers/Postgres integration test that checks SQL columns only.

## Functional Requirements

### FR-1: hasFeedback tri-state filter
**Acceptance criteria:**
- `null` returns all articles.
- `true` returns articles with PrecisionScore OR StyleScore set (including only one of the two).
- `false` returns only articles with both scores null (partially scored excluded).
- `TotalCount` reflects the filtered count.

### FR-2: requestedBy filter
**Acceptance criteria:**
- Exact string match returns only that user's articles.
- Null / empty / whitespace value applies no filter.
- Composes with hasFeedback.

### FR-3: sortBy switch
**Acceptance criteria:**
- `PrecisionScore` asc/desc, `StyleScore` asc/desc, and default (`CreatedAt`, also for unknown values) asc/desc each order correctly.
- Paging (`page`, `pageSize`) skips/takes correctly with TotalCount unaffected by paging.

### FR-4: GetFeedbackStatsAsync
**Acceptance criteria:**
- Empty table returns `ArticleFeedbackStats(0, 0, null, null)`.
- Populated table returns total count, count with any score, and averages of non-null scores.

## Non-Functional Requirements
### NFR-1: Performance
Tests run in-process (EF InMemory), well under a second each.
### NFR-2: Security
N/A.

## Data Model
Existing `Article` entity; no changes.

## API / Interface Design
None.

## Dependencies
`Microsoft.EntityFrameworkCore.InMemory` (already referenced), xUnit, FluentAssertions.

## Out of Scope
Production code changes; Postgres integration tests; other repository methods.

## Open Questions
None.

## Status: COMPLETE
