# Encapsulate Article Feedback Submission in the Domain Entity — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the three direct property assignments in `SubmitArticleFeedbackHandler` (`PrecisionScore`, `StyleScore`, `FeedbackComment`) into a new `Article.SubmitFeedback(precisionScore, styleScore, comment)` method on the domain entity so the entity owns its full lifecycle (matching the existing `MarkAsResearching`, `MarkAsWriting`, `MarkAsGenerated`, `MarkAsFailed` pattern).

**Architecture:** Pure refactor. Behavior, public API, response shape, error codes, persistence path, and guard logic are unchanged. The handler still runs the four guards (not-found, forbidden, not-generated, already-submitted) and still calls `SaveChangesAsync`; it just delegates the in-memory mutation to a new entity method. Public setters on the three feedback properties remain (EF Core change-tracking and the existing handler-test factory rely on them).

**Tech Stack:** .NET 8, C#, MediatR, EF Core, xUnit, FluentAssertions.

---

## File Structure

| File | Action | Responsibility |
|------|--------|----------------|
| `backend/src/Anela.Heblo.Domain/Features/Article/Article.cs` | Modify | Add `SubmitFeedback` method after `MarkAsFailed`. |
| `backend/src/Anela.Heblo.Application/Features/Article/UseCases/SubmitFeedback/SubmitArticleFeedbackHandler.cs` | Modify | Replace lines 57–59 (three property assignments) with one method call. |
| `backend/test/Anela.Heblo.Tests/Domain/Article/ArticleTests.cs` | Create | New entity-level unit tests for `SubmitFeedback`. Establishes the canonical location for `Article` domain tests (none exist today). |
| `backend/test/Anela.Heblo.Tests/Article/UseCases/SubmitArticleFeedbackHandlerTests.cs` | Unchanged | Must pass as-is; proves behavior preservation. |

Test path follows the existing repo convention (`Domain/{Feature}/{Entity}Tests.cs`), per the architecture review amendment — every other domain-entity test lives there (`Domain/Logistics`, `Domain/Purchase`, `Domain/Catalog`, `Domain/Marketing`).

---

### Task 1: Add failing unit tests for `Article.SubmitFeedback`

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Domain/Article/ArticleTests.cs`

- [ ] **Step 1: Write the failing tests**

Create the file with the following exact content:

```csharp
using Anela.Heblo.Domain.Features.Article;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Domain.Article
{
    public class ArticleTests
    {
        [Fact]
        public void SubmitFeedback_AssignsAllThreeProperties()
        {
            var article = new Heblo.Domain.Features.Article.Article
            {
                Id = Guid.NewGuid(),
                Topic = "topic",
            };

            article.PrecisionScore.Should().BeNull();
            article.StyleScore.Should().BeNull();
            article.FeedbackComment.Should().BeNull();

            article.SubmitFeedback(precisionScore: 5, styleScore: 4, comment: "Great");

            article.PrecisionScore.Should().Be(5);
            article.StyleScore.Should().Be(4);
            article.FeedbackComment.Should().Be("Great");
        }

        [Fact]
        public void SubmitFeedback_NullComment_IsAllowed()
        {
            var article = new Heblo.Domain.Features.Article.Article
            {
                Id = Guid.NewGuid(),
                Topic = "topic",
            };

            var act = () => article.SubmitFeedback(precisionScore: 3, styleScore: 3, comment: null);

            act.Should().NotThrow();
            article.PrecisionScore.Should().Be(3);
            article.StyleScore.Should().Be(3);
            article.FeedbackComment.Should().BeNull();
        }
    }
}
```

Notes for the engineer:
- The namespace `Anela.Heblo.Tests.Domain.Article` collides with the entity's type name (`Anela.Heblo.Domain.Features.Article.Article`). The fully-qualified `Heblo.Domain.Features.Article.Article` in the test resolves the ambiguity from inside this namespace — keep it as written. (This mirrors how other domain tests handle name collisions in this repo.)
- `using Anela.Heblo.Domain.Features.Article;` keeps the `using` block sorted and matches the style of `Domain/Marketing/MarketingActionConstructorTests.cs`.

- [ ] **Step 2: Run the new tests and verify they fail**

Run from the repo root:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Domain.Article.ArticleTests" \
  --nologo
```

Expected result: **build failure** with an error similar to `CS1061: 'Article' does not contain a definition for 'SubmitFeedback'`. This is the RED step — we have a test that proves the method doesn't yet exist.

- [ ] **Step 3: Commit the failing test**

```bash
git add backend/test/Anela.Heblo.Tests/Domain/Article/ArticleTests.cs
git commit -m "test: add failing unit tests for Article.SubmitFeedback"
```

---

### Task 2: Add `SubmitFeedback` to the `Article` entity (make tests pass)

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Article/Article.cs:48`

- [ ] **Step 1: Add the `SubmitFeedback` method immediately after `MarkAsFailed`**

In `backend/src/Anela.Heblo.Domain/Features/Article/Article.cs`, locate `MarkAsFailed` (currently lines 44–48). Add a blank line after its closing brace and append the new method so the bottom of the file becomes:

```csharp
    public void MarkAsFailed(string errorMessage)
    {
        Status = ArticleStatus.Failed;
        ErrorMessage = errorMessage.Length > 500 ? errorMessage[..500] : errorMessage;
    }

    public void SubmitFeedback(int precisionScore, int styleScore, string? comment)
    {
        PrecisionScore = precisionScore;
        StyleScore = styleScore;
        FeedbackComment = comment;
    }
}
```

Do not modify any other member. Public setters on `PrecisionScore`, `StyleScore`, and `FeedbackComment` (lines 24–26) must stay — EF Core change-tracking and the existing handler-test factory depend on them.

- [ ] **Step 2: Run the new entity tests and verify they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Domain.Article.ArticleTests" \
  --nologo
```

Expected: `Passed: 2, Failed: 0`. This is the GREEN step.

- [ ] **Step 3: Commit the entity change**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Article/Article.cs
git commit -m "feat(article): add SubmitFeedback method to entity"
```

---

### Task 3: Replace direct property assignment in `SubmitArticleFeedbackHandler`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Article/UseCases/SubmitFeedback/SubmitArticleFeedbackHandler.cs:57-59`

- [ ] **Step 1: Replace the three assignments with a single method call**

In `SubmitArticleFeedbackHandler.cs`, find this block (lines 57–59, immediately before `await _repository.SaveChangesAsync(ct);`):

```csharp
        article.PrecisionScore = request.PrecisionScore;
        article.StyleScore = request.StyleScore;
        article.FeedbackComment = request.Comment;
```

Replace it with exactly one line:

```csharp
        article.SubmitFeedback(request.PrecisionScore, request.StyleScore, request.Comment);
```

Do not touch the four guard blocks (lines 26–55), the `SaveChangesAsync` call, or the `SubmitArticleFeedbackResponse` construction. The response continues to read `PrecisionScore`, `StyleScore`, and `FeedbackComment` straight off `article`.

- [ ] **Step 2: Run the handler test suite and verify it still passes unchanged**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~SubmitArticleFeedbackHandlerTests" \
  --nologo
```

Expected: all seven existing scenarios pass (article-missing, other-user, same-display-name-different-identifier, same-identifier-different-display-name, null-`RequestedBy`, not-generated, already-submitted, plus happy path) with no test edits. If anything fails, the handler edit diverged from the spec — revert the change and re-read the diff.

- [ ] **Step 3: Commit the handler change**

```bash
git add backend/src/Anela.Heblo.Application/Features/Article/UseCases/SubmitFeedback/SubmitArticleFeedbackHandler.cs
git commit -m "refactor(article): delegate feedback mutation to entity"
```

---

### Task 4: Final validation (build, format, full test pass)

**Files:** none modified — this task is verification only.

- [ ] **Step 1: Build the backend solution**

```bash
dotnet build backend/Anela.Heblo.sln --nologo
```

Expected: `Build succeeded` with `0 Warning(s)` and `0 Error(s)` introduced by this change.

- [ ] **Step 2: Format the codebase**

```bash
dotnet format backend/Anela.Heblo.sln
```

Expected: completes with no errors. If formatting changes were applied to either edited file, stage and amend the latest commit:

```bash
git status
# If only the two edited files changed:
git add backend/src/Anela.Heblo.Domain/Features/Article/Article.cs \
        backend/src/Anela.Heblo.Application/Features/Article/UseCases/SubmitFeedback/SubmitArticleFeedbackHandler.cs \
        backend/test/Anela.Heblo.Tests/Domain/Article/ArticleTests.cs
git commit -m "style: apply dotnet format"
```

If `git status` shows unrelated files changed by `dotnet format`, do not include them — they belong to a separate cleanup.

- [ ] **Step 3: Run the full backend test project once**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --nologo
```

Expected: `Failed: 0`. The new two tests pass, the seven existing `SubmitArticleFeedbackHandlerTests` pass, and nothing else regressed.

- [ ] **Step 4: Confirm scope — only the planned files changed**

```bash
git log --oneline origin/main..HEAD
git diff --stat origin/main..HEAD
```

Expected: three (or four, including the optional format commit) commits touching exactly:
- `backend/src/Anela.Heblo.Domain/Features/Article/Article.cs` (+6 lines)
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/SubmitFeedback/SubmitArticleFeedbackHandler.cs` (−3 +1 lines)
- `backend/test/Anela.Heblo.Tests/Domain/Article/ArticleTests.cs` (new file, ~35 lines)
- `docs/superpowers/plans/2026-06-08-encapsulate-article-feedback-submission.md` (this plan)

No frontend files, no migrations, no DI changes, no OpenAPI regeneration, no controller edits. If anything else shows up, stop and investigate before pushing.

---

## Out of Scope (reminder)

Do not, in this plan, attempt any of the following — they are explicitly excluded by the spec and arch review:

- Tighten setter visibility on `PrecisionScore` / `StyleScore` / `FeedbackComment` to `private set` or `init`.
- Add range validation, normalization, or guard logic inside `SubmitFeedback` (range `[1, 5]` is enforced via `[Range]` on the request DTO).
- Move handler guards (`ArticleNotFound`, `Forbidden`, `ArticleNotGenerated`, `ArticleFeedbackAlreadySubmitted`) into the entity.
- Add a `FeedbackSubmittedAt` timestamp.
- Publish a domain event from `SubmitFeedback`.
- Any frontend or OpenAPI/TypeScript-client changes — the HTTP surface is unchanged.

If any of these feel tempting while implementing, stop and raise a separate spec — they each warrant their own plan.
