# Implementation: feedback-sort-paging-tests

## What was implemented
Added sort and paging tests for `ArticleRepository.GetFeedbackPagedAsync` to `ArticleRepositoryFeedbackQueryTests`.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Article/Persistence/ArticleRepositoryFeedbackQueryTests.cs` — new `SeedScoredAsync` fixture, `SortBy_OrdersResults` Theory (PrecisionScore/StyleScore/CreatedAt/Bogus x asc/desc), paging test (page 2, size 2 of 5 -> items 3-4, TotalCount 5)

## How to verify
`cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~ArticleRepositoryFeedbackQueryTests"` (17 passed)

## Notes
Developer and reviewer subagents were not spawned (no Task tool); the orchestrator executed the task directly, as in the previous task.

## Status
DONE
