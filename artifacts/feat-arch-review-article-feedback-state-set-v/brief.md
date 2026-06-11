## Module
Article

## Finding

The `Article` entity encapsulates all status transitions through explicit methods:

```csharp
// backend/src/Anela.Heblo.Domain/Features/Article/Article.cs
public void MarkAsResearching() => Status = ArticleStatus.Researching;
public void MarkAsWriting() => Status = ArticleStatus.Writing;
public void MarkAsGenerated(string? title, string? htmlContent) { ... }
public void MarkAsFailed(string errorMessage) { ... }  // includes truncation logic
```

However, `SubmitArticleFeedbackHandler` bypasses this pattern and mutates the entity's feedback properties directly from the handler:

```csharp
// backend/src/Anela.Heblo.Application/Features/Article/UseCases/SubmitFeedback/SubmitArticleFeedbackHandler.cs lines 57-59
article.PrecisionScore = request.PrecisionScore;
article.StyleScore = request.StyleScore;
article.FeedbackComment = request.Comment;
```

The business rule "an article can receive feedback exactly once, from its requester, when in Generated state" is split across the handler (the guard checks) and direct property assignment — rather than being expressed through an entity method like `article.SubmitFeedback(precisionScore, styleScore, comment)`.

## Why it matters

- **Anemic domain model**: the development guidelines explicitly state "Don't create anemic domain models — Put behavior in entities." The entity already follows this for status transitions; feedback is the inconsistent exception.
- **Scattered future logic**: `MarkAsFailed` already embeds truncation logic inside the entity. If feedback scores ever need normalization, bounds enforcement, or a `FeedbackSubmittedAt` timestamp, that logic would land in the handler rather than the entity — contradicting the established pattern.
- **Discoverability**: a developer looking at `Article.cs` to understand what state transitions the entity supports will not find feedback submission there.

## Suggested fix

Add a `SubmitFeedback` method to the `Article` entity in `backend/src/Anela.Heblo.Domain/Features/Article/Article.cs`:

```csharp
public void SubmitFeedback(int precisionScore, int styleScore, string? comment)
{
    PrecisionScore = precisionScore;
    StyleScore = styleScore;
    FeedbackComment = comment;
}
```

Replace the three direct property assignments in `SubmitArticleFeedbackHandler` (lines 57–59) with:

```csharp
article.SubmitFeedback(request.PrecisionScore, request.StyleScore, request.Comment);
```

No change to the guard logic or response shape is needed.

---
_Filed by daily arch-review routine on 2026-06-07._