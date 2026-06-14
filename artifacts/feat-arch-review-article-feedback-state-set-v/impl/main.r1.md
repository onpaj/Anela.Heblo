All four tasks complete. Writing the output artifact.

---

# Implementation: Encapsulate Article Feedback Submission in Domain Entity

## What was implemented

A pure refactor that moves the three direct property assignments (`PrecisionScore`, `StyleScore`, `FeedbackComment`) from `SubmitArticleFeedbackHandler` into a new `Article.SubmitFeedback(precisionScore, styleScore, comment)` method on the domain entity. The entity now fully owns its lifecycle, consistent with `MarkAsResearching`, `MarkAsWriting`, `MarkAsGenerated`, `MarkAsFailed`. All behavior, error codes, HTTP shape, and persistence path are unchanged.

## Files created/modified

- `backend/src/Anela.Heblo.Domain/Features/Article/Article.cs` — added `SubmitFeedback(int precisionScore, int styleScore, string? comment)` method after `MarkAsFailed` (+6 lines)
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/SubmitFeedback/SubmitArticleFeedbackHandler.cs` — replaced three direct property assignments with one `article.SubmitFeedback(...)` call (−3 +1 lines)
- `backend/test/Anela.Heblo.Tests/Domain/Article/ArticleTests.cs` — new entity-level test file (created, ~40 lines)
- `docs/superpowers/plans/2026-06-08-encapsulate-article-feedback-submission.md` — implementation plan (committed)

## Tests

- `backend/test/Anela.Heblo.Tests/Domain/Article/ArticleTests.cs`:
  - `SubmitFeedback_AssignsAllThreeProperties` — verifies all three properties are set correctly
  - `SubmitFeedback_NullComment_IsAllowed` — verifies null comment does not throw
- `backend/test/Anela.Heblo.Tests/Article/UseCases/SubmitArticleFeedbackHandlerTests.cs` — all 8 existing scenarios pass unchanged

## How to verify

```bash
# Entity tests (2 pass)
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Domain.Article.ArticleTests" --nologo

# Handler tests (8 pass)
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~SubmitArticleFeedbackHandlerTests" --nologo

# Full build
dotnet build backend/Anela.Heblo.sln --nologo
```

## Notes

- The test file uses fully-qualified `Heblo.Domain.Features.Article.Article` (not just `Article`) because the test namespace `Anela.Heblo.Tests.Domain.Article` shadows the entity type — this is required, not a style issue.
- 38 Docker/Testcontainers integration tests fail in the full suite because Docker is not running in this environment; these are pre-existing and unrelated to this change.
- No DI changes, no migrations, no OpenAPI regeneration, no frontend changes.

## PR Summary

Encapsulates article feedback submission in the `Article` domain entity by introducing a `SubmitFeedback(precisionScore, styleScore, comment)` method, replacing the three direct property assignments that lived in `SubmitArticleFeedbackHandler`. This closes the last anemic-model gap in the entity, aligning feedback with the established `MarkAs*` lifecycle-method pattern. All existing handler tests pass unchanged; two new focused entity-level tests establish the canonical `Domain/Article/ArticleTests.cs` location.

### Changes
- `backend/src/Anela.Heblo.Domain/Features/Article/Article.cs` — added `SubmitFeedback` method after `MarkAsFailed`
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/SubmitFeedback/SubmitArticleFeedbackHandler.cs` — single `article.SubmitFeedback(...)` call replaces three property assignments
- `backend/test/Anela.Heblo.Tests/Domain/Article/ArticleTests.cs` — new: two xUnit facts verifying the entity method in isolation
- `docs/superpowers/plans/2026-06-08-encapsulate-article-feedback-submission.md` — implementation plan

## Status
DONE