## Module / File
`backend/src/Anela.Heblo.Persistence/Features/Article/ArticleRepository.cs`

## Coverage
Line coverage: 23% (22/95 lines, filter threshold: 60%)

## What's not tested
`GetFeedbackPagedAsync` has a tri-state `hasFeedback` filter (`null` → all articles, `true` → any score present, `false` → both scores absent). The `false` branch uses a compound AND predicate (`PrecisionScore == null && StyleScore == null`) while the `true` branch uses OR — swapping them silently inverts the filter. The `requestedBy` string filter and the four-case `sortBy` switch expression (`PrecisionScore`, `StyleScore`, default ascending/descending) are also uncovered. `GetFeedbackStatsAsync` is entirely untested: the GroupBy aggregation and the fallback `new ArticleFeedbackStats(0, 0, null, null)` for an empty table.

## Why it matters
A bug in the tri-state filter would return the wrong article set to the feedback review page — e.g. showing all articles when "no feedback" is selected, or vice versa. The fallback stats object would return zeros on an empty table but could also hide a query translation failure that only surfaces on first data.

## Suggested approach
Unit tests against an in-memory or Sqlite DbContext seeded with a small article set. Cover: `hasFeedback=null` returns all; `hasFeedback=true` returns only articles with at least one score; `hasFeedback=false` returns only articles with both scores null; `requestedBy` filter matches exact string; `GetFeedbackStatsAsync` on empty table returns zero-count record. ~2h.

---
_Filed by weekly coverage-gap routine on 2026-10-05. Based on CI run #36876655065 (923c24940cf65d6ce38a8b56d75709bba333ca91)._
