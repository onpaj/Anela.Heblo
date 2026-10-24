# Implementation: feedback-filter-tests

## What was implemented
New `ArticleRepositoryFeedbackQueryTests` (InMemory EF) with seed fixtures A-E and tests for the hasFeedback tri-state and requestedBy filters of `GetFeedbackPagedAsync`.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Article/Persistence/ArticleRepositoryFeedbackQueryTests.cs` — new test class

## Tests
null -> all 5; true -> {A,B,C}; false -> {D,E} (partial excluded); requestedBy user1 -> {A,B,E}; null/empty/whitespace requestedBy -> all; false + user2 -> {D}.

## How to verify
`cd backend && dotnet test test/Anela.Heblo.Tests --filter "FullyQualifiedName~ArticleRepositoryFeedbackQueryTests"` (8 passed)

## Notes
Developer and reviewer subagents were not spawned; the orchestrator executed the task directly.

## PR Summary
Added repository-level tests covering the tri-state hasFeedback and requestedBy filters of ArticleRepository.GetFeedbackPagedAsync.

### Changes
- `backend/test/Anela.Heblo.Tests/Article/Persistence/ArticleRepositoryFeedbackQueryTests.cs` — filter tests

## Status
DONE
