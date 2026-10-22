# Implementation: feedback-stats-tests

## What was implemented
Added two tests for `ArticleRepository.GetFeedbackStatsAsync` to `ArticleRepositoryFeedbackQueryTests`: empty table returns `ArticleFeedbackStats(0, 0, null, null)`; populated fixture (A-E) returns Total 5, WithFeedback 3, AvgPrecision 4.0, AvgStyle 3.0 (averages over non-null values only).

## Files modified
- `backend/test/Anela.Heblo.Tests/Article/Persistence/ArticleRepositoryFeedbackQueryTests.cs`

## Notes
InMemory translated `GroupBy(_ => 1)` fine; no Sqlite switch needed. Developer/reviewer subagents not spawned; orchestrator executed directly, as in earlier tasks. The task text said precision 4,2 / style 5,3; the actual shared fixture has precision 5,3 (avg 4) and style 4,2 (avg 3), which is what is asserted.

## How to verify
`cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~ArticleRepositoryFeedbackQueryTests"` (19 passed); `dotnet build` 0 errors; `dotnet format --verify-no-changes` clean.

## Status
DONE
