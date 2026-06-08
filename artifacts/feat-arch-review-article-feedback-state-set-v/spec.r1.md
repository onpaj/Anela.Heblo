I have enough context to write the spec. The brief is clear and surgical — moving three property assignments into an entity method.

# Specification: Encapsulate Article Feedback Submission in the Domain Entity

## Summary
Move the direct mutation of `Article.PrecisionScore`, `Article.StyleScore`, and `Article.FeedbackComment` out of `SubmitArticleFeedbackHandler` and into a new `Article.SubmitFeedback(precisionScore, styleScore, comment)` method on the domain entity. This brings feedback submission in line with the entity's existing state-transition pattern (`MarkAsResearching`, `MarkAsWriting`, `MarkAsGenerated`, `MarkAsFailed`) and removes the last anemic-model gap in this entity.

## Background
`backend/src/Anela.Heblo.Domain/Features/Article/Article.cs` already encapsulates every other lifecycle change as a method on the entity. Only feedback submission (`SubmitArticleFeedbackHandler.cs:57-59`) reaches into the entity and sets three public setters directly. The development guidelines (`docs/architecture/development_guidelines.md`) explicitly call out "Don't create anemic domain models — put behavior in entities." Aligning this one call site is a small, low-risk refactor that pays compounding interest the next time feedback gains behavior (timestamps, bounds enforcement, normalization, an `IsFeedbackSubmitted` predicate, or domain events).

This is a pure refactor: behavior, public API, response shape, error codes, and guard logic are unchanged.

## Functional Requirements

### FR-1: Add `SubmitFeedback` method to the `Article` entity
A new public instance method `SubmitFeedback(int precisionScore, int styleScore, string? comment)` is added to `Anela.Heblo.Domain.Features.Article.Article`. The method assigns the three values to `PrecisionScore`, `StyleScore`, and `FeedbackComment` respectively. It performs no validation and raises no exceptions — guard logic remains in the handler in this revision (see Out of Scope for the rationale).

**Acceptance criteria:**
- A method with signature `public void SubmitFeedback(int precisionScore, int styleScore, string? comment)` exists on `Article`.
- After calling `article.SubmitFeedback(p, s, c)`, `article.PrecisionScore == p`, `article.StyleScore == s`, and `article.FeedbackComment == c`.
- The method is grouped with the other state-transition methods (`MarkAsResearching`, `MarkAsWriting`, `MarkAsGenerated`, `MarkAsFailed`) in `Article.cs`.
- No other members of `Article` are modified; public setters on the three feedback properties remain in place (EF Core and existing tests rely on them).

### FR-2: Replace direct property assignment in `SubmitArticleFeedbackHandler`
The three lines at `SubmitArticleFeedbackHandler.cs:57-59` are replaced with a single call to the new entity method.

**Acceptance criteria:**
- `SubmitArticleFeedbackHandler.Handle` calls `article.SubmitFeedback(request.PrecisionScore, request.StyleScore, request.Comment)` exactly once, immediately after the four guard checks (article-found, requester-match, status-is-Generated, no-prior-feedback) and immediately before `await _repository.SaveChangesAsync(ct)`.
- No `article.PrecisionScore = …`, `article.StyleScore = …`, or `article.FeedbackComment = …` assignments remain in the handler.
- The guard block (lines 26–55), the call to `SaveChangesAsync`, and the response construction are unchanged.
- The response continues to populate `PrecisionScore`, `StyleScore`, and `FeedbackComment` from `article` after the call.

### FR-3: Preserve all existing behavior and error semantics
This is a refactor, not a behavior change. Every observable contract — HTTP shape, error codes, status code paths, validation, and persistence — stays identical.

**Acceptance criteria:**
- The existing test suite in `backend/test/Anela.Heblo.Tests/Article/UseCases/SubmitArticleFeedbackHandlerTests.cs` passes unmodified.
- The seven existing test scenarios continue to produce the same `ErrorCode` / `Success` / persisted-field values: article-missing, other-user, same-display-name-different-identifier, same-identifier-different-display-name, null-`RequestedBy`, not-generated, already-submitted, and happy path.
- No changes to `SubmitArticleFeedbackRequest`, `SubmitArticleFeedbackResponse`, `IArticleRepository`, or any controller.

### FR-4: Add a unit test for the new entity method
A small focused test verifies `Article.SubmitFeedback` in isolation, mirroring the structure of any existing entity-method tests (or introducing the first one if none exist).

**Acceptance criteria:**
- Test file `backend/test/Anela.Heblo.Tests/Article/Domain/ArticleTests.cs` (or the existing equivalent if one is found during implementation) contains at least one xUnit `[Fact]` named `SubmitFeedback_AssignsAllThreeProperties` that:
  - Arranges a new `Article` with `PrecisionScore`, `StyleScore`, and `FeedbackComment` all `null`.
  - Acts: calls `article.SubmitFeedback(5, 4, "Great")`.
  - Asserts (FluentAssertions): all three properties hold the supplied values.
- A second `[Fact]` named `SubmitFeedback_NullComment_IsAllowed` confirms `article.SubmitFeedback(3, 3, null)` does not throw and leaves `FeedbackComment` null.

## Non-Functional Requirements

### NFR-1: Performance
No measurable change. The method is a trivial property-setter aggregation called once per `SubmitArticleFeedback` HTTP request (already a low-volume endpoint).

### NFR-2: Security
No change. Authorization, ownership check, and status guard continue to run in the handler before the entity method is invoked. The entity method is unreachable from outside an authenticated, authorized handler.

### NFR-3: Maintainability
Behavior co-located with state — a developer reading `Article.cs` sees the full lifecycle (status transitions + feedback) without jumping to handlers. New feedback-related logic (timestamps, normalization, bounds enforcement, domain events) will have an obvious home.

### NFR-4: Backward compatibility
The public HTTP contract, the OpenAPI surface, and the generated TypeScript client are unchanged. No client regeneration is required.

## Data Model
No schema changes. The three columns on the `Articles` table (`PrecisionScore`, `StyleScore`, `FeedbackComment`) are written by the same EF Core change tracking on the same entity instance — only the C# call path that mutates the in-memory entity changes.

## API / Interface Design

**Domain (new):**
```csharp
// Anela.Heblo.Domain.Features.Article.Article
public void SubmitFeedback(int precisionScore, int styleScore, string? comment)
{
    PrecisionScore = precisionScore;
    StyleScore = styleScore;
    FeedbackComment = comment;
}
```

**Application (changed):**
```csharp
// SubmitArticleFeedbackHandler.Handle — replace lines 57-59
article.SubmitFeedback(request.PrecisionScore, request.StyleScore, request.Comment);
```

**HTTP / OpenAPI:** unchanged. The `POST /api/articles/{id}/feedback` (or equivalent) endpoint, its request/response DTOs, and all error codes (`ArticleNotFound`, `Forbidden`, `ArticleNotGenerated`, `ArticleFeedbackAlreadySubmitted`) remain exactly as today.

## Dependencies
- `backend/src/Anela.Heblo.Domain/Features/Article/Article.cs` — entity edit.
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/SubmitFeedback/SubmitArticleFeedbackHandler.cs` — handler edit.
- `backend/test/Anela.Heblo.Tests/Article/UseCases/SubmitArticleFeedbackHandlerTests.cs` — existing tests, expected to pass unchanged.
- No new NuGet packages. No infrastructure changes. No database migration.

## Out of Scope
- **Moving the guard checks into the entity.** The handler retains the four guards (not-found, forbidden, not-generated, already-submitted) because they require collaborators (`ICurrentUserService`, `IArticleRepository`) and need to return typed error responses. Pushing them into the entity would require introducing domain exceptions and a translation layer in the handler — a larger refactor that is explicitly *not* requested by the brief.
- **Making feedback properties `private set` / `init`.** The brief does not request encapsulating the setters; EF Core mapping and the existing test factory (`CreateArticle(..., precisionScore: 4, ...)`) read/write them directly. Tightening setter visibility is a separate, larger change.
- **Adding a `FeedbackSubmittedAt` timestamp.** Mentioned in the brief as a *future* possibility — not part of this change.
- **Normalization or bounds enforcement inside `SubmitFeedback`.** Range validation (1–5) lives on the request DTO via `[Range(1, 5)]` and is enforced by ASP.NET model binding. Duplicating it in the entity is unnecessary right now.
- **Converting `Article` to a domain-events-publishing aggregate.** Out of scope for this refactor.
- **Frontend changes.** None — the HTTP contract is unchanged.

## Open Questions
None.

## Status: COMPLETE