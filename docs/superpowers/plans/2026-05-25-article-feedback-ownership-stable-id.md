# Article Feedback Ownership: Stable User Identifier Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the display-name-based ownership check in the Article feedback flow with the stable user identifier (`CurrentUser.GetIdentifier()`), and backfill existing rows via an admin endpoint that resolves display names through Microsoft Graph.

**Architecture:** Two surgical one-line changes — `GenerateArticleHandler` (write site) and `SubmitArticleFeedbackHandler` (compare site) both switch from `currentUser.Name` to `currentUser.GetIdentifier()`. A null-owner guard is added at the compare site. A new admin MediatR command `BackfillArticleRequestedByCommand` resolves historical display names through `IGraphService.GetGroupMembersAsync` and overwrites the column in-place when resolution is unambiguous; ambiguous and unresolved rows are reported, never overwritten. Idempotency is guaranteed by shape detection (GUID or email-shaped values are skipped).

**Tech Stack:** C# / .NET 8, MediatR, EF Core, xUnit, FluentAssertions, Moq, Microsoft Graph (via existing `IGraphService`), PostgreSQL.

---

## File Structure

**Modify:**
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/GenerateArticle/GenerateArticleHandler.cs` — swap `currentUser.Name` → `currentUser.GetIdentifier()` on line 46
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/SubmitFeedback/SubmitArticleFeedbackHandler.cs` — swap compare to `user.GetIdentifier()`, add null-owner guard, delete stale comment on line 34
- `backend/src/Anela.Heblo.API/Controllers/ArticlesController.cs` — add admin backfill endpoint
- `backend/test/Anela.Heblo.Tests/Article/UseCases/GenerateArticleHandlerTests.cs` — update RequestedBy expectations
- `backend/test/Anela.Heblo.Tests/Article/UseCases/SubmitArticleFeedbackHandlerTests.cs` — rewrite SetCurrentUser helper, add collision/rename/null-owner tests

**Create:**
- `backend/src/Anela.Heblo.Application/Features/Article/Admin/BackfillArticleRequestedByCommand.cs` — MediatR command DTO
- `backend/src/Anela.Heblo.Application/Features/Article/Admin/BackfillArticleRequestedByResponse.cs` — result DTO with counts + unresolved list
- `backend/src/Anela.Heblo.Application/Features/Article/Admin/BackfillArticleRequestedByHandler.cs` — handler logic
- `backend/src/Anela.Heblo.Application/Features/Article/Admin/UnresolvedArticleRow.cs` — per-row failure record
- `backend/test/Anela.Heblo.Tests/Article/Admin/BackfillArticleRequestedByHandlerTests.cs` — unit tests for the backfill handler
- `docs/operations/article-requestedby-backfill.md` — operator runbook for the backfill

**Audit-only (read, classify, document in PR — no code change expected):**
- `backend/src/Anela.Heblo.Persistence/Features/Article/ArticleRepository.cs:75-76` — filter parameter `requestedBy`
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/GetFeedbackList/GetArticleFeedbackListHandler.cs:49` — DTO projection
- `frontend/src/components/feedback/adapters/useArticleFeedbackAdapter.ts:12,20` — adapter already feeds `userId` into `requestedBy`

---

### Task 1: Refactor SubmitArticleFeedbackHandlerTests fixture + add failing collision/rename/null-owner tests

This task locks in the *real* behaviour we want before touching the handler. The new `SetCurrentUser` helper takes an explicit identifier so the test reader cannot miss the distinction (per arch-review §Spec Amendment 5).

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Article/UseCases/SubmitArticleFeedbackHandlerTests.cs`

- [ ] **Step 1: Replace the SetCurrentUser/CreateArticle helpers and rewrite existing tests + add three new failing tests**

Replace the entire file content with:

```csharp
using Anela.Heblo.Application.Features.Article.UseCases.SubmitFeedback;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Article;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Moq;
using DomainArticle = Anela.Heblo.Domain.Features.Article.Article;

namespace Anela.Heblo.Tests.Article.UseCases;

public class SubmitArticleFeedbackHandlerTests
{
    private const string AliceIdentifier = "alice-oid-1111";
    private const string BobIdentifier = "bob-oid-2222";

    private readonly Mock<IArticleRepository> _repository = new();
    private readonly Mock<ICurrentUserService> _currentUser = new();

    private SubmitArticleFeedbackHandler CreateHandler() =>
        new(_repository.Object, _currentUser.Object);

    private static DomainArticle CreateArticle(
        string? requestedBy = AliceIdentifier,
        ArticleStatus status = ArticleStatus.Generated,
        int? precisionScore = null,
        int? styleScore = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            Topic = "Topic",
            RequestedBy = requestedBy,
            Status = status,
            PrecisionScore = precisionScore,
            StyleScore = styleScore,
        };

    private void SetCurrentUser(string identifier, string? displayName = null) =>
        _currentUser.Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser(
                Id: identifier,
                Name: displayName ?? "Display-" + identifier,
                Email: null,
                IsAuthenticated: true));

    [Fact]
    public async Task Handle_ArticleMissing_ReturnsArticleNotFound()
    {
        var request = new SubmitArticleFeedbackRequest
        {
            ArticleId = Guid.NewGuid(),
            PrecisionScore = 4,
            StyleScore = 5,
        };
        SetCurrentUser(AliceIdentifier);
        _repository.Setup(r => r.GetForUpdateAsync(request.ArticleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DomainArticle?)null);

        var response = await CreateHandler().Handle(request, default);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ArticleNotFound);
    }

    [Fact]
    public async Task Handle_OtherUser_ReturnsForbidden()
    {
        var article = CreateArticle(requestedBy: AliceIdentifier);
        var request = new SubmitArticleFeedbackRequest
        {
            ArticleId = article.Id,
            PrecisionScore = 4,
            StyleScore = 5,
        };
        SetCurrentUser(BobIdentifier);
        _repository.Setup(r => r.GetForUpdateAsync(article.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(article);

        var response = await CreateHandler().Handle(request, default);

        response.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_SameDisplayNameDifferentIdentifier_ReturnsForbidden()
    {
        // NFR-1: two users with identical display name "Jan Novák"
        // but different Entra OIDs must not access each other's articles.
        var article = CreateArticle(requestedBy: AliceIdentifier);
        var request = new SubmitArticleFeedbackRequest
        {
            ArticleId = article.Id,
            PrecisionScore = 4,
            StyleScore = 5,
        };
        SetCurrentUser(identifier: BobIdentifier, displayName: "Jan Novák");
        // Note: article.RequestedBy stores AliceIdentifier, not the display name,
        // even though a display-name comparison would have wrongly matched here
        // if we had stored "Jan Novák" for both users.
        _repository.Setup(r => r.GetForUpdateAsync(article.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(article);

        var response = await CreateHandler().Handle(request, default);

        response.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_SameIdentifierDifferentDisplayName_Succeeds()
    {
        // NFR-2: a user whose Entra display name changed between generating
        // and submitting feedback must still be recognised as owner.
        var article = CreateArticle(requestedBy: AliceIdentifier);
        var request = new SubmitArticleFeedbackRequest
        {
            ArticleId = article.Id,
            PrecisionScore = 5,
            StyleScore = 4,
            Comment = "Renamed user",
        };
        SetCurrentUser(identifier: AliceIdentifier, displayName: "Alice (renamed)");
        _repository.Setup(r => r.GetForUpdateAsync(article.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(article);

        var response = await CreateHandler().Handle(request, default);

        response.Success.Should().BeTrue();
        article.PrecisionScore.Should().Be(5);
        article.FeedbackComment.Should().Be("Renamed user");
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_NullRequestedBy_ReturnsForbidden()
    {
        // FR-2 amendment: anonymous-created article (RequestedBy is null) must
        // never be claimable, regardless of caller identity.
        var article = CreateArticle(requestedBy: null);
        var request = new SubmitArticleFeedbackRequest
        {
            ArticleId = article.Id,
            PrecisionScore = 4,
            StyleScore = 5,
        };
        SetCurrentUser(AliceIdentifier);
        _repository.Setup(r => r.GetForUpdateAsync(article.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(article);

        var response = await CreateHandler().Handle(request, default);

        response.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_ArticleNotGenerated_ReturnsArticleNotGenerated()
    {
        var article = CreateArticle(status: ArticleStatus.Writing);
        var request = new SubmitArticleFeedbackRequest
        {
            ArticleId = article.Id,
            PrecisionScore = 3,
            StyleScore = 3,
        };
        SetCurrentUser(AliceIdentifier);
        _repository.Setup(r => r.GetForUpdateAsync(article.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(article);

        var response = await CreateHandler().Handle(request, default);

        response.ErrorCode.Should().Be(ErrorCodes.ArticleNotGenerated);
    }

    [Fact]
    public async Task Handle_AlreadySubmitted_ReturnsAlreadySubmittedConflict()
    {
        var article = CreateArticle(precisionScore: 4);
        var request = new SubmitArticleFeedbackRequest
        {
            ArticleId = article.Id,
            PrecisionScore = 5,
            StyleScore = 5,
        };
        SetCurrentUser(AliceIdentifier);
        _repository.Setup(r => r.GetForUpdateAsync(article.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(article);

        var response = await CreateHandler().Handle(request, default);

        response.ErrorCode.Should().Be(ErrorCodes.ArticleFeedbackAlreadySubmitted);
    }

    [Fact]
    public async Task Handle_HappyPath_PersistsFeedbackAndReturnsValues()
    {
        var article = CreateArticle();
        var request = new SubmitArticleFeedbackRequest
        {
            ArticleId = article.Id,
            PrecisionScore = 5,
            StyleScore = 4,
            Comment = "Great",
        };
        SetCurrentUser(AliceIdentifier);
        _repository.Setup(r => r.GetForUpdateAsync(article.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(article);

        var response = await CreateHandler().Handle(request, default);

        response.Success.Should().BeTrue();
        response.PrecisionScore.Should().Be(5);
        response.StyleScore.Should().Be(4);
        response.FeedbackComment.Should().Be("Great");
        article.PrecisionScore.Should().Be(5);
        article.StyleScore.Should().Be(4);
        article.FeedbackComment.Should().Be("Great");
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 2: Run the new tests to confirm they fail**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~SubmitArticleFeedbackHandlerTests" \
  --no-build
```

If the build fails because of the test changes, run `dotnet build backend/Anela.Heblo.sln` first; then re-run the filtered test command.

Expected: `Handle_SameDisplayNameDifferentIdentifier_ReturnsForbidden`, `Handle_SameIdentifierDifferentDisplayName_Succeeds`, and `Handle_NullRequestedBy_ReturnsForbidden` FAIL. The remaining tests should pass (because the rewritten helper now puts the identifier-shaped value in both `article.RequestedBy` and `currentUser.Name` for the legacy-shape tests via the `Display-` prefix — see below).

Note: `Handle_OtherUser_ReturnsForbidden` and the other passing tests rely on the fact that the handler currently compares `article.RequestedBy` (e.g. `"alice-oid-1111"`) to `currentUser.Name` (e.g. `"Display-alice-oid-1111"`). Since these strings differ, the handler still returns Forbidden in the OtherUser case and the test still passes. The Happy/AlreadySubmitted/NotGenerated/NotFound tests do not exercise the comparison meaningfully (or set the same identifier in both slots), so they continue to pass.

The newly added tests fail because:
- `SameDisplayName...`: handler returns Success today (current `Name` comparison would match "Jan Novák" if seeded, but the helper seeds different names so this test wants the *identifier* mismatch to drive Forbidden — which it doesn't until the swap)
- Actually, re-examining: with the rewritten helper, `Name` is `"Display-bob-oid-2222"` so the current-code `Name`-comparison against `"alice-oid-1111"` returns Forbidden anyway. **The collision test as written above does not fail against current code.** Strengthen by explicitly seeding the colliding display name:

Replace the `SetCurrentUser` call in `Handle_SameDisplayNameDifferentIdentifier_ReturnsForbidden` so that the *current* handler (which compares Names) would succeed, and also seed `article.RequestedBy` to match that display name. Update the test as follows (overwrite Steps 1 block for this test):

```csharp
    [Fact]
    public async Task Handle_SameDisplayNameDifferentIdentifier_ReturnsForbidden()
    {
        // NFR-1: two users sharing display name "Jan Novák" but with different
        // Entra OIDs must not access each other's articles. To prove the test
        // exercises the identifier (not the name) we deliberately make the
        // stored RequestedBy LOOK like an identifier — equal to Alice's OID —
        // while Bob has the same display name as the legacy-stored name would.
        var article = CreateArticle(requestedBy: AliceIdentifier);
        var request = new SubmitArticleFeedbackRequest
        {
            ArticleId = article.Id,
            PrecisionScore = 4,
            StyleScore = 5,
        };
        // Bob's display name is intentionally set to AliceIdentifier so a
        // legacy Name-based compare would WRONGLY succeed.
        SetCurrentUser(identifier: BobIdentifier, displayName: AliceIdentifier);
        _repository.Setup(r => r.GetForUpdateAsync(article.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(article);

        var response = await CreateHandler().Handle(request, default);

        response.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
```

Re-run the test. Now `Handle_SameDisplayNameDifferentIdentifier_ReturnsForbidden` FAILS against current code (handler compares Names; Bob's Name == AliceIdentifier == article.RequestedBy → wrongly succeeds), and `Handle_SameIdentifierDifferentDisplayName_Succeeds` FAILS (handler compares Names; Alice's renamed Name "Alice (renamed)" != article.RequestedBy "alice-oid-1111"), and `Handle_NullRequestedBy_ReturnsForbidden` FAILS only if the handler currently has a code path where matching null names somehow succeeds — verify with the run below.

Expected output: at least two of the three new tests FAIL.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Article/UseCases/SubmitArticleFeedbackHandlerTests.cs
git commit -m "test: cover identifier-based ownership in submit-article-feedback"
```

---

### Task 2: Swap SubmitArticleFeedbackHandler to GetIdentifier(), add null-owner guard, delete stale comment

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Article/UseCases/SubmitFeedback/SubmitArticleFeedbackHandler.cs`

- [ ] **Step 1: Apply the swap and guard**

Edit lines 33–41 of `SubmitArticleFeedbackHandler.cs`. Replace:

```csharp
        // RequestedBy stores currentUser.Name (set in GenerateArticleHandler). Compare by Name.
        var user = _currentUser.GetCurrentUser();
        if (!string.Equals(article.RequestedBy, user.Name, StringComparison.Ordinal))
        {
            return new SubmitArticleFeedbackResponse(
                ErrorCodes.Forbidden,
                new Dictionary<string, string> { { "id", request.ArticleId.ToString() } });
        }
```

with:

```csharp
        var user = _currentUser.GetCurrentUser();
        if (article.RequestedBy is null ||
            !string.Equals(article.RequestedBy, user.GetIdentifier(), StringComparison.Ordinal))
        {
            return new SubmitArticleFeedbackResponse(
                ErrorCodes.Forbidden,
                new Dictionary<string, string> { { "id", request.ArticleId.ToString() } });
        }
```

- [ ] **Step 2: Run the SubmitArticleFeedbackHandlerTests and confirm all pass**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~SubmitArticleFeedbackHandlerTests"
```

Expected: all eight tests PASS, including the three new ones.

- [ ] **Step 3: Format and commit**

```bash
dotnet format backend/Anela.Heblo.sln \
  --include backend/src/Anela.Heblo.Application/Features/Article/UseCases/SubmitFeedback/SubmitArticleFeedbackHandler.cs
git add backend/src/Anela.Heblo.Application/Features/Article/UseCases/SubmitFeedback/SubmitArticleFeedbackHandler.cs
git commit -m "fix(article): authorize feedback against stable identifier, reject null owner"
```

---

### Task 3: Update GenerateArticleHandlerTests for identifier semantics (failing)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Article/UseCases/GenerateArticleHandlerTests.cs`

- [ ] **Step 1: Update the existing happy-path test and add an explicit-identifier test**

Edit `GenerateArticleHandlerTests.cs`. Two edits required.

**Edit A — change line 73 to expect the identifier:**

Replace:
```csharp
        captured.RequestedBy.Should().Be("John Doe");
```

with:
```csharp
        captured.RequestedBy.Should().Be("user-id");
```

**Edit B — append a new explicit test right after `Handle_AnonymousUser_RequestedByIsNull`:**

Insert this test method directly before the closing brace of the class:

```csharp
    [Fact]
    public async Task Handle_AuthenticatedUserWithoutId_FallsBackToEmail()
    {
        // GetIdentifier() returns Id ?? Email ?? "system". When Id is missing
        // (e.g. a future auth path that only exposes preferred_username), the
        // Email is what we want to persist — never "system" for an authenticated
        // user.
        _currentUserService
            .Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser(Id: null, Name: "Some One", Email: "some.one@example.com", IsAuthenticated: true));

        DomainArticle? captured = null;
        _repository
            .Setup(r => r.AddAsync(It.IsAny<DomainArticle>(), It.IsAny<CancellationToken>()))
            .Callback<DomainArticle, CancellationToken>((a, _) => captured = a)
            .Returns(Task.CompletedTask);

        await CreateHandler().Handle(new GenerateArticleRequest { Topic = "Topic" }, default);

        captured!.RequestedBy.Should().Be("some.one@example.com");
    }
```

- [ ] **Step 2: Run the tests to confirm Edit A fails**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GenerateArticleHandlerTests"
```

Expected: `Handle_HappyPath_CreatesArticleWithMappedFields` FAILS (current handler writes "John Doe", not "user-id"); `Handle_AuthenticatedUserWithoutId_FallsBackToEmail` FAILS (writes `Name == "Some One"`, not the email). `Handle_AnonymousUser_RequestedByIsNull` and `Handle_PersistsAndEnqueuesHangfireJob` PASS.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Article/UseCases/GenerateArticleHandlerTests.cs
git commit -m "test: expect stable identifier in generated article RequestedBy"
```

---

### Task 4: Swap GenerateArticleHandler write site

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Article/UseCases/GenerateArticle/GenerateArticleHandler.cs`

- [ ] **Step 1: Apply the one-line swap**

Edit line 46 of `GenerateArticleHandler.cs`. Replace:

```csharp
            RequestedBy = currentUser.IsAuthenticated ? currentUser.Name : null,
```

with:

```csharp
            RequestedBy = currentUser.IsAuthenticated ? currentUser.GetIdentifier() : null,
```

Verify the file still has the `using Anela.Heblo.Domain.Features.Users;` directive (it does — line 3) so `GetIdentifier()` resolves.

- [ ] **Step 2: Run GenerateArticleHandlerTests and confirm all pass**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GenerateArticleHandlerTests"
```

Expected: all four tests PASS.

- [ ] **Step 3: Format and commit**

```bash
dotnet format backend/Anela.Heblo.sln \
  --include backend/src/Anela.Heblo.Application/Features/Article/UseCases/GenerateArticle/GenerateArticleHandler.cs
git add backend/src/Anela.Heblo.Application/Features/Article/UseCases/GenerateArticle/GenerateArticleHandler.cs
git commit -m "fix(article): persist stable identifier in RequestedBy on generation"
```

---

### Task 5: Audit other RequestedBy comparison sites (FR-4)

This task is read-only and verifies that no other code path silently relies on the old display-name semantics. Findings get recorded in the PR description.

**Files:** none modified.

- [ ] **Step 1: Repository-wide search for RequestedBy**

Run:
```bash
grep -rn --include='*.cs' --include='*.ts' --include='*.tsx' RequestedBy backend/src backend/test frontend/src
```

Classify each hit into one of these buckets and capture in a scratch note for the PR description:

| Location | Bucket | Reason |
|---|---|---|
| `GenerateArticleHandler.cs:46` | write site | Changed in Task 4 |
| `SubmitArticleFeedbackHandler.cs` (compare) | ownership check | Changed in Task 2 |
| `ArticleRepository.cs:75-76` (filter) | pass-through filter | Operator now passes an identifier; no logic change needed |
| `GetArticleFeedbackListHandler.cs:49` (projection) | DTO emission | DTO value semantics flip from "name" to "identifier"; UI display follow-up tracked in Task 11 |
| `GetArticleFeedbackListHandlerTests.cs:23,43` | test fixture | Cosmetic; "alice" still works as an opaque string; do not change unless the assertion's intent becomes unclear |
| Test files in `backend/test/Anela.Heblo.Tests/Article/...` | test fixtures | Already handled in Tasks 1 & 3 |
| `frontend/src/components/feedback/adapters/useArticleFeedbackAdapter.ts:12,20` | adapter | Already feeds `userId` into `requestedBy`; semantics now match |
| `frontend/src/api/generated/api-client.ts` | generated | Regenerates automatically on backend build; no manual edit |
| `frontend/src/api/hooks/useArticles.ts` | hook | Passes through; no logic change |
| `frontend/src/components/feedback/.../GenericFeedbackDetailModal.tsx` (if present) | display | Now renders an opaque OID — tracked as follow-up (Task 11) |

- [ ] **Step 2: Confirm no other ownership-style comparison uses RequestedBy**

Run:
```bash
grep -rn --include='*.cs' -E 'RequestedBy.*(==|Equals)' backend/src backend/test
```

Expected: only the two sites already covered (`SubmitArticleFeedbackHandler.cs` and any test setup that compares `captured.RequestedBy.Should().Be(...)`). If anything else surfaces, classify it and either update it or document in the PR why it is correct.

- [ ] **Step 3: Save audit findings as a scratch file for the PR description**

Write the findings to `docs/superpowers/plans/2026-05-25-article-feedback-ownership-audit.md` for inclusion in the PR description. This file should be committed alongside the audit.

```bash
git add docs/superpowers/plans/2026-05-25-article-feedback-ownership-audit.md
git commit -m "docs: capture RequestedBy audit findings for ownership change"
```

If the audit file is empty (no additional findings), skip the commit and note "audit complete, no additional sites" in the PR description.

---

### Task 6: Create BackfillArticleRequestedByCommand DTO + response types

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Article/Admin/BackfillArticleRequestedByCommand.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Article/Admin/BackfillArticleRequestedByResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Article/Admin/UnresolvedArticleRow.cs`

- [ ] **Step 1: Write the command DTO**

Create `BackfillArticleRequestedByCommand.cs`:

```csharp
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.Article.Admin;

public sealed class BackfillArticleRequestedByCommand
    : IRequest<BackfillArticleRequestedByResponse>
{
    /// <summary>
    /// Entra group ID whose members are candidates for display-name → OID resolution.
    /// Required.
    /// </summary>
    public string GroupId { get; set; } = string.Empty;

    /// <summary>
    /// When true (default), the command runs the resolution pass but does NOT
    /// persist any change to the database. Use for previewing.
    /// </summary>
    public bool DryRun { get; set; } = true;
}
```

- [ ] **Step 2: Write the response DTO**

Create `BackfillArticleRequestedByResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Article.Admin;

public sealed class BackfillArticleRequestedByResponse : BaseResponse
{
    public BackfillArticleRequestedByResponse() { }

    public BackfillArticleRequestedByResponse(
        ErrorCodes errorCode,
        Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }

    public int Total { get; set; }
    public int AlreadyMigrated { get; set; }
    public int Resolved { get; set; }
    public int Ambiguous { get; set; }
    public int Unresolved { get; set; }
    public bool WasDryRun { get; set; }
    public List<UnresolvedArticleRow> UnresolvedRows { get; set; } = new();
}
```

- [ ] **Step 3: Write the per-row failure record**

Create `UnresolvedArticleRow.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Article.Admin;

public sealed class UnresolvedArticleRow
{
    public Guid ArticleId { get; set; }
    public string OriginalValue { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
```

- [ ] **Step 4: Verify `BaseResponse` import — confirm namespace + constructor signature**

Run:
```bash
grep -n "class BaseResponse" backend/src/Anela.Heblo.Application/Shared/*.cs
```

If `BaseResponse` is located elsewhere or has a different constructor (e.g. takes only `ErrorCodes` without `Dictionary`), adjust the response class's constructor body to match. If it does not exist at all, replace the inheritance with explicit properties: `bool Success`, `string? ErrorCode`, `Dictionary<string, string>? Parameters` to match the shape of `SubmitArticleFeedbackResponse`.

- [ ] **Step 5: Build to confirm compilation**

Run:
```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: build succeeds. If `BaseResponse` doesn't exist or has a different shape, fix the response DTO before continuing.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Article/Admin/
git commit -m "feat(article): add BackfillArticleRequestedByCommand DTOs"
```

---

### Task 7: Write the failing BackfillArticleRequestedByHandler unit tests

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Article/Admin/BackfillArticleRequestedByHandlerTests.cs`

- [ ] **Step 1: Create the test file**

Write the file:

```csharp
using Anela.Heblo.Application.Features.Article.Admin;
using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Article;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using DomainArticle = Anela.Heblo.Domain.Features.Article.Article;

namespace Anela.Heblo.Tests.Article.Admin;

public class BackfillArticleRequestedByHandlerTests
{
    private const string GroupId = "marketing-group-id";

    private readonly Mock<IArticleAdminRepository> _repository = new();
    private readonly Mock<IGraphService> _graph = new();

    private BackfillArticleRequestedByHandler CreateHandler() =>
        new(_repository.Object, _graph.Object, NullLogger<BackfillArticleRequestedByHandler>.Instance);

    private static DomainArticle Row(string requestedBy)
        => new() { Id = Guid.NewGuid(), Topic = "Topic", RequestedBy = requestedBy };

    private static UserDto Member(string id, string displayName)
        => new() { Id = id, DisplayName = displayName, Email = $"{displayName}@example.com" };

    [Fact]
    public async Task Handle_MissingGroupId_ReturnsValidationError()
    {
        var request = new BackfillArticleRequestedByCommand { GroupId = "", DryRun = true };

        var response = await CreateHandler().Handle(request, default);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ValidationError);
    }

    [Fact]
    public async Task Handle_SkipsGuidShapedRowsAsAlreadyMigrated()
    {
        var alreadyMigrated = Row(Guid.NewGuid().ToString());
        _repository.Setup(r => r.ListWithRequestedByAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DomainArticle> { alreadyMigrated });
        _graph.Setup(g => g.GetGroupMembersAsync(GroupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserDto>());

        var response = await CreateHandler().Handle(
            new BackfillArticleRequestedByCommand { GroupId = GroupId, DryRun = true }, default);

        response.AlreadyMigrated.Should().Be(1);
        response.Resolved.Should().Be(0);
        response.UnresolvedRows.Should().BeEmpty();
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_SkipsEmailShapedRowsAsAlreadyMigrated()
    {
        var alreadyMigrated = Row("john@example.com");
        _repository.Setup(r => r.ListWithRequestedByAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DomainArticle> { alreadyMigrated });
        _graph.Setup(g => g.GetGroupMembersAsync(GroupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserDto>());

        var response = await CreateHandler().Handle(
            new BackfillArticleRequestedByCommand { GroupId = GroupId, DryRun = true }, default);

        response.AlreadyMigrated.Should().Be(1);
        response.Resolved.Should().Be(0);
    }

    [Fact]
    public async Task Handle_UniqueDisplayNameMatch_ResolvesRow()
    {
        var row = Row("Jan Novák");
        _repository.Setup(r => r.ListWithRequestedByAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DomainArticle> { row });
        _graph.Setup(g => g.GetGroupMembersAsync(GroupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserDto> { Member("jan-oid", "Jan Novák") });

        var response = await CreateHandler().Handle(
            new BackfillArticleRequestedByCommand { GroupId = GroupId, DryRun = false }, default);

        response.Resolved.Should().Be(1);
        response.WasDryRun.Should().BeFalse();
        row.RequestedBy.Should().Be("jan-oid");
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_AmbiguousDisplayName_LeavesRowAndReports()
    {
        var row = Row("Jan Novák");
        _repository.Setup(r => r.ListWithRequestedByAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DomainArticle> { row });
        _graph.Setup(g => g.GetGroupMembersAsync(GroupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserDto>
            {
                Member("jan-oid-a", "Jan Novák"),
                Member("jan-oid-b", "Jan Novák"),
            });

        var response = await CreateHandler().Handle(
            new BackfillArticleRequestedByCommand { GroupId = GroupId, DryRun = false }, default);

        response.Ambiguous.Should().Be(1);
        response.Resolved.Should().Be(0);
        response.UnresolvedRows.Should().ContainSingle(u =>
            u.ArticleId == row.Id && u.OriginalValue == "Jan Novák" && u.Reason.Contains("ambiguous"));
        row.RequestedBy.Should().Be("Jan Novák");
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_UnknownDisplayName_LeavesRowAndReports()
    {
        var row = Row("Ghost User");
        _repository.Setup(r => r.ListWithRequestedByAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DomainArticle> { row });
        _graph.Setup(g => g.GetGroupMembersAsync(GroupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserDto> { Member("someone-oid", "Someone Else") });

        var response = await CreateHandler().Handle(
            new BackfillArticleRequestedByCommand { GroupId = GroupId, DryRun = false }, default);

        response.Unresolved.Should().Be(1);
        response.UnresolvedRows.Should().ContainSingle(u =>
            u.OriginalValue == "Ghost User" && u.Reason.Contains("no match"));
        row.RequestedBy.Should().Be("Ghost User");
    }

    [Fact]
    public async Task Handle_DryRun_DoesNotSaveResolvedRows()
    {
        var row = Row("Jan Novák");
        _repository.Setup(r => r.ListWithRequestedByAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DomainArticle> { row });
        _graph.Setup(g => g.GetGroupMembersAsync(GroupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserDto> { Member("jan-oid", "Jan Novák") });

        var response = await CreateHandler().Handle(
            new BackfillArticleRequestedByCommand { GroupId = GroupId, DryRun = true }, default);

        response.Resolved.Should().Be(1);
        response.WasDryRun.Should().BeTrue();
        // Dry-run leaves the in-memory entity in its resolved state but never persists.
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_MixedSet_CountsCorrectly()
    {
        var rows = new List<DomainArticle>
        {
            Row(Guid.NewGuid().ToString()),    // already migrated (GUID)
            Row("ondra@example.com"),           // already migrated (email)
            Row("Jan Novák"),                  // resolved
            Row("Petra Dvořáková"),            // ambiguous
            Row("Ghost User"),                  // unresolved
        };
        _repository.Setup(r => r.ListWithRequestedByAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);
        _graph.Setup(g => g.GetGroupMembersAsync(GroupId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserDto>
            {
                Member("jan-oid", "Jan Novák"),
                Member("petra-oid-1", "Petra Dvořáková"),
                Member("petra-oid-2", "Petra Dvořáková"),
            });

        var response = await CreateHandler().Handle(
            new BackfillArticleRequestedByCommand { GroupId = GroupId, DryRun = false }, default);

        response.Total.Should().Be(5);
        response.AlreadyMigrated.Should().Be(2);
        response.Resolved.Should().Be(1);
        response.Ambiguous.Should().Be(1);
        response.Unresolved.Should().Be(1);
        response.UnresolvedRows.Should().HaveCount(2);
    }
}
```

- [ ] **Step 2: Run the tests to confirm they fail to compile (handler + repository interface do not exist yet)**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~BackfillArticleRequestedByHandlerTests" \
  --no-restore
```

Expected: COMPILATION FAILS with "type or namespace `BackfillArticleRequestedByHandler` does not exist" and "type or namespace `IArticleAdminRepository` does not exist". This is the failing-state checkpoint.

- [ ] **Step 3: Commit the failing tests**

```bash
git add backend/test/Anela.Heblo.Tests/Article/Admin/BackfillArticleRequestedByHandlerTests.cs
git commit -m "test: cover BackfillArticleRequestedByHandler resolution + idempotency"
```

---

### Task 8: Implement IArticleAdminRepository + BackfillArticleRequestedByHandler

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Article/IArticleAdminRepository.cs`
- Create: `backend/src/Anela.Heblo.Persistence/Features/Article/ArticleAdminRepository.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Article/Admin/BackfillArticleRequestedByHandler.cs`

- [ ] **Step 1: Define the admin repository interface in Domain**

Create `IArticleAdminRepository.cs`:

```csharp
using DomainArticle = Anela.Heblo.Domain.Features.Article.Article;

namespace Anela.Heblo.Domain.Features.Article;

/// <summary>
/// Admin-only repository for one-off operations on the Articles table. Not for runtime use.
/// </summary>
public interface IArticleAdminRepository
{
    Task<List<DomainArticle>> ListWithRequestedByAsync(CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
```

- [ ] **Step 2: Implement the admin repository in Persistence**

Create `ArticleAdminRepository.cs`:

```csharp
using Anela.Heblo.Domain.Features.Article;
using Microsoft.EntityFrameworkCore;
using DomainArticle = Anela.Heblo.Domain.Features.Article.Article;

namespace Anela.Heblo.Persistence.Features.Article;

public sealed class ArticleAdminRepository : IArticleAdminRepository
{
    private readonly ApplicationDbContext _context;

    public ArticleAdminRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<List<DomainArticle>> ListWithRequestedByAsync(CancellationToken ct = default) =>
        _context.Articles
            .Where(a => a.RequestedBy != null)
            .ToListAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        _context.SaveChangesAsync(ct);
}
```

Note: this repository does NOT use `AsNoTracking()`, because the backfill needs to mutate and persist the loaded entities.

- [ ] **Step 3: Implement the backfill handler**

Create `BackfillArticleRequestedByHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.UserManagement.Contracts;
using Anela.Heblo.Application.Features.UserManagement.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Article;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Article.Admin;

public sealed class BackfillArticleRequestedByHandler
    : IRequestHandler<BackfillArticleRequestedByCommand, BackfillArticleRequestedByResponse>
{
    private readonly IArticleAdminRepository _repository;
    private readonly IGraphService _graph;
    private readonly ILogger<BackfillArticleRequestedByHandler> _logger;

    public BackfillArticleRequestedByHandler(
        IArticleAdminRepository repository,
        IGraphService graph,
        ILogger<BackfillArticleRequestedByHandler> logger)
    {
        _repository = repository;
        _graph = graph;
        _logger = logger;
    }

    public async Task<BackfillArticleRequestedByResponse> Handle(
        BackfillArticleRequestedByCommand request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.GroupId))
        {
            return new BackfillArticleRequestedByResponse(
                ErrorCodes.ValidationError,
                new Dictionary<string, string> { { "field", "GroupId" } });
        }

        var members = await _graph.GetGroupMembersAsync(request.GroupId, ct);
        var byDisplayName = members
            .GroupBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var rows = await _repository.ListWithRequestedByAsync(ct);
        var response = new BackfillArticleRequestedByResponse
        {
            Total = rows.Count,
            WasDryRun = request.DryRun,
        };

        var anyResolved = false;

        foreach (var row in rows)
        {
            var original = row.RequestedBy!; // non-null filter applied in repository

            if (LooksLikeIdentifier(original))
            {
                response.AlreadyMigrated++;
                _logger.LogInformation(
                    "Article {ArticleId} RequestedBy={Value} already looks like an identifier; skipping.",
                    row.Id, original);
                continue;
            }

            if (!byDisplayName.TryGetValue(original, out var matches))
            {
                response.Unresolved++;
                response.UnresolvedRows.Add(new UnresolvedArticleRow
                {
                    ArticleId = row.Id,
                    OriginalValue = original,
                    Reason = "no match in Graph group members",
                });
                _logger.LogWarning(
                    "Article {ArticleId} RequestedBy={Value} has no match in group {GroupId}.",
                    row.Id, original, request.GroupId);
                continue;
            }

            if (matches.Count > 1)
            {
                response.Ambiguous++;
                response.UnresolvedRows.Add(new UnresolvedArticleRow
                {
                    ArticleId = row.Id,
                    OriginalValue = original,
                    Reason = $"ambiguous: {matches.Count} group members share this display name",
                });
                _logger.LogWarning(
                    "Article {ArticleId} RequestedBy={Value} is ambiguous ({Count} matches).",
                    row.Id, original, matches.Count);
                continue;
            }

            var match = matches[0];
            row.RequestedBy = match.Id;
            anyResolved = true;
            response.Resolved++;
            _logger.LogInformation(
                "Article {ArticleId} resolved: {DisplayName} -> {Id}.",
                row.Id, original, match.Id);
        }

        if (anyResolved && !request.DryRun)
        {
            await _repository.SaveChangesAsync(ct);
        }

        return response;
    }

    /// <summary>
    /// Heuristic idempotency check. Stable identifiers in this codebase are
    /// either Entra OID (GUID) or email-shaped fallback. Display names never
    /// match either shape, so this is safe in practice.
    /// </summary>
    private static bool LooksLikeIdentifier(string value)
    {
        if (Guid.TryParse(value, out _))
        {
            return true;
        }

        return value.Contains('@', StringComparison.Ordinal);
    }
}
```

- [ ] **Step 4: Run the backfill handler tests and confirm all pass**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~BackfillArticleRequestedByHandlerTests"
```

Expected: all eight tests PASS.

- [ ] **Step 5: Format and commit**

```bash
dotnet format backend/Anela.Heblo.sln \
  --include backend/src/Anela.Heblo.Application/Features/Article/Admin/ \
            backend/src/Anela.Heblo.Domain/Features/Article/IArticleAdminRepository.cs \
            backend/src/Anela.Heblo.Persistence/Features/Article/ArticleAdminRepository.cs
git add backend/src/Anela.Heblo.Application/Features/Article/Admin/BackfillArticleRequestedByHandler.cs \
        backend/src/Anela.Heblo.Domain/Features/Article/IArticleAdminRepository.cs \
        backend/src/Anela.Heblo.Persistence/Features/Article/ArticleAdminRepository.cs
git commit -m "feat(article): implement BackfillArticleRequestedByHandler"
```

---

### Task 9: Register IArticleAdminRepository in DI

**Files:**
- Modify: a Persistence DI registration extension (search for one and add the binding there)

- [ ] **Step 1: Locate the existing repository registration site**

Run:
```bash
grep -rn "AddScoped<IArticleRepository" backend/src
```

Expected: one match in a Persistence-module extension file, e.g. `backend/src/Anela.Heblo.Persistence/...Module.cs` or `backend/src/Anela.Heblo.API/Program.cs`. Open that file.

- [ ] **Step 2: Add the admin-repository binding next to the existing one**

Insert a new line directly below the `services.AddScoped<IArticleRepository, ArticleRepository>();` registration:

```csharp
services.AddScoped<IArticleAdminRepository, ArticleAdminRepository>();
```

Add the missing `using` statement if the file doesn't already import `Anela.Heblo.Domain.Features.Article;` or `Anela.Heblo.Persistence.Features.Article;`.

- [ ] **Step 3: Build to confirm**

Run:
```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add <the file you edited>
git commit -m "chore(di): register IArticleAdminRepository"
```

---

### Task 10: Expose backfill via ArticlesController as a SuperUser-gated admin endpoint

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/ArticlesController.cs`

- [ ] **Step 1: Add the admin endpoint**

Insert the following inside `ArticlesController`, directly after the `FeedbackList` action (line 103):

```csharp
    [HttpPost("admin/backfill-requested-by")]
    [Authorize(Roles = AuthorizationConstants.Roles.SuperUser)]
    public async Task<ActionResult<BackfillArticleRequestedByResponse>> BackfillRequestedBy(
        [FromBody] BackfillArticleRequestedByCommand request,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(request, ct);
        return HandleResponse(result);
    }
```

Add the matching `using` directive at the top of the file:

```csharp
using Anela.Heblo.Application.Features.Article.Admin;
```

`AuthorizationConstants` is already imported via `using Anela.Heblo.Domain.Features.Authorization;` on line 8.

- [ ] **Step 2: Build to confirm**

Run:
```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: build succeeds.

- [ ] **Step 3: Format and commit**

```bash
dotnet format backend/Anela.Heblo.sln \
  --include backend/src/Anela.Heblo.API/Controllers/ArticlesController.cs
git add backend/src/Anela.Heblo.API/Controllers/ArticlesController.cs
git commit -m "feat(api): expose admin backfill endpoint for article RequestedBy"
```

---

### Task 11: Write the operator runbook

**Files:**
- Create: `docs/operations/article-requestedby-backfill.md`

- [ ] **Step 1: Create the docs/operations directory and the runbook file**

Run:
```bash
mkdir -p docs/operations
```

Then write `docs/operations/article-requestedby-backfill.md`:

```markdown
# Article RequestedBy backfill

One-off migration to convert `Articles.RequestedBy` from display names (legacy)
to stable Entra identifiers (current). After the ownership-fix release ships,
existing rows still hold display names — without this backfill, their original
authors cannot submit feedback on their own articles.

## Prerequisites

1. **Row-count baseline.** Run on production (or the target environment):
   ```sql
   SELECT count(*) FROM "Articles" WHERE "RequestedBy" IS NOT NULL;
   ```
   If the count exceeds ~10,000, contact engineering before running the
   backfill — the current implementation loads all rows into memory in a
   single batch.

2. **Backup the Articles table.** The backfill writes display names out
   in-place; the original values are not preserved post-write. Take a
   `pg_dump` of the `Articles` table immediately before running.
   ```bash
   pg_dump -Fc -t '"Articles"' \
     -h <host> -U <user> -d <db> \
     -f articles-backup-$(date +%Y%m%d-%H%M).dump
   ```

3. **Confirm the Entra group.** Find the group ID whose members historically
   generated articles (typically the marketing group). Verify membership in
   Azure Portal → Microsoft Entra ID → Groups.

4. **Confirm Graph permissions.** The application identity needs
   `GroupMember.Read.All` (or `User.Read.All`) — already granted in production
   per `GraphService` usage. Re-verify in the target tenant's app
   registration before running.

5. **Confirm SuperUser role.** The endpoint is gated by the `super_user` role
   on the calling user.

## Running the backfill

The backfill is an authenticated HTTP POST to the production API. Acquire an
access token for an account that holds the `super_user` role.

### Step 1 — Dry run (preview)

```bash
curl -X POST \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"groupId":"<entra-group-id>","dryRun":true}' \
  https://<api-host>/api/articles/admin/backfill-requested-by
```

The response includes:
- `total`: how many rows have a non-null `RequestedBy`
- `alreadyMigrated`: rows whose stored value already looks like an identifier
  (GUID or contains `@`) — skipped
- `resolved`: rows that mapped to exactly one group member
- `ambiguous`: rows whose display name matched multiple group members —
  left untouched, included in `unresolvedRows`
- `unresolved`: rows whose display name matched no group member — left
  untouched, included in `unresolvedRows`
- `wasDryRun`: `true` for this call

Review `unresolvedRows`. For each row, decide whether to triage manually
(see "Manual triage" below) before the write run.

### Step 2 — Write run

```bash
curl -X POST \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"groupId":"<entra-group-id>","dryRun":false}' \
  https://<api-host>/api/articles/admin/backfill-requested-by
```

Persists every `resolved` row in a single `SaveChanges`. Ambiguous and
unresolved rows are still left untouched.

### Step 3 — Verify

```sql
SELECT count(*) FROM "Articles"
WHERE "RequestedBy" IS NOT NULL
  AND "RequestedBy" !~ '^[0-9a-fA-F\-]{36}$'
  AND "RequestedBy" NOT LIKE '%@%';
```

Should equal `ambiguous + unresolved` from the last write-run response.

## Idempotency

The handler skips any row whose `RequestedBy` is GUID-shaped or contains `@`.
Re-running the backfill after a successful run is a no-op for already-migrated
rows; only rows still holding a display name are reconsidered.

## Manual triage

For ambiguous rows, decide via context (which marketing person worked on
that topic, when, etc.) which OID to write, then:

```sql
UPDATE "Articles" SET "RequestedBy" = '<oid>' WHERE "Id" = '<article-id>';
```

For unresolved rows (e.g. authors who have left the org), either find their
old OID in Azure Portal's deleted-users view, or leave the row owner-less.
Leaving it owner-less means the original author cannot submit feedback even
if they later rejoin under the same OID — accepted per the spec.

## Rollback

There is no in-product rollback. Restore the `Articles` table from the
backup taken in Prerequisites step 2:

```bash
pg_restore -h <host> -U <user> -d <db> --data-only -t '"Articles"' \
  articles-backup-<timestamp>.dump
```
```

- [ ] **Step 2: Commit**

```bash
git add docs/operations/article-requestedby-backfill.md
git commit -m "docs(ops): runbook for Article RequestedBy backfill"
```

---

### Task 12: Add a frontend UX follow-up tracker

The arch review flagged a user-visible regression: `GenericFeedbackDetailModal.tsx` (and any other UI that renders `requestedBy` raw) will now display an opaque OID instead of "Jan Novák". The fix is to resolve OID → display name in `GetArticleFeedbackListHandler.cs:49` via the same Graph member map (cached). That work is out of scope for this PR but must not be lost.

**Files:**
- Modify or create: `docs/features/article-generation.md` (append a "Follow-ups" section if not already present)

- [ ] **Step 1: Confirm whether GenericFeedbackDetailModal renders requestedBy**

Run:
```bash
grep -rn 'requestedBy' frontend/src/components/feedback/
```

Expected: at least one `.tsx` file renders the field. Capture the file path(s) for the follow-up note.

- [ ] **Step 2: Append a follow-up note to docs/features/article-generation.md**

Open `docs/features/article-generation.md`. Find the existing structure and append a section at the end:

```markdown
## Follow-ups

### Resolve RequestedBy identifier → display name (UX)

After 2026-05-25, `Article.RequestedBy` stores a stable Entra identifier
(OID or email) instead of a display name. The feedback list and detail
views currently render the raw value, which now appears as an opaque OID.

Fix scope:
- `GetArticleFeedbackListHandler.cs` — resolve OID → display name via
  `IGraphService.GetGroupMembersAsync` (cached) when projecting
  `ArticleFeedbackSummary.RequestedBy`.
- Frontend renderers (e.g. `GenericFeedbackDetailModal.tsx`) consume the
  resolved name; no UI logic change required if the backend swap is
  transparent.

Tracked separately from the ownership/security fix that introduced the
regression.
```

- [ ] **Step 3: Commit**

```bash
git add docs/features/article-generation.md
git commit -m "docs(article): note follow-up for RequestedBy display resolution"
```

---

### Task 13: Final verification

- [ ] **Step 1: Full backend build and test**

Run:
```bash
dotnet build backend/Anela.Heblo.sln
dotnet test backend/Anela.Heblo.sln
```

Expected: build succeeds; all tests pass. If any unrelated test breaks, investigate before continuing — do not silence it.

- [ ] **Step 2: Frontend build and lint**

Run:
```bash
cd frontend && npm run build && npm run lint && cd ..
```

Expected: build and lint pass. The OpenAPI client regenerates on backend build; if the lint flags an unused export or anything tied to the (new) admin endpoint, address it.

- [ ] **Step 3: Format the entire backend**

Run:
```bash
dotnet format backend/Anela.Heblo.sln
```

Stage and commit any formatting fixes:

```bash
git add -A
git diff --staged --quiet || git commit -m "chore: dotnet format"
```

- [ ] **Step 4: Sanity-check the diff against the spec**

Run:
```bash
git log --oneline main..HEAD
```

Confirm there is a commit covering each numbered FR/NFR in the spec:
- FR-1 (write-site swap) — Task 4
- FR-2 (read-site swap + null-owner guard) — Task 2
- FR-3 (backfill) — Tasks 6–11
- FR-4 (audit) — Task 5
- NFR-1 (collision test) — Task 1
- NFR-2 (rename test) — Task 1
- NFR-3 (migration integrity — ambiguous/unknown left untouched) — Task 7
- NFR-4 (performance — single batch acceptable up to ~10k rows, runbook covers escalation) — Task 11

---

## Self-Review Notes

### Spec coverage
- **FR-1** Task 4 ✓
- **FR-2** Task 2 ✓ (includes null-owner guard from arch-review amendment 2)
- **FR-3** Tasks 6–11 ✓ (standalone admin endpoint, not EF migration — per arch-review amendment 1)
- **FR-4** Task 5 ✓
- **NFR-1** Task 1 (`Handle_SameDisplayNameDifferentIdentifier_ReturnsForbidden`) ✓
- **NFR-2** Task 1 (`Handle_SameIdentifierDifferentDisplayName_Succeeds`) ✓
- **NFR-3** Task 7 (ambiguous + unknown tests assert no overwrite + no SaveChanges) ✓
- **NFR-4** Task 11 (runbook explicit about row-count threshold) ✓
- **Arch amendment 3** (store-vs-compare consistency) — both sites now call `GetIdentifier()`; Tasks 2 and 4 enforce.
- **Arch amendment 5** (explicit fixture for collision) — Task 1's `SetCurrentUser(identifier, displayName)` makes the distinction unmissable.
- **Arch amendment 6** (UX follow-up tracked) — Task 12.

### Placeholder scan
None. Every code step has full source. Every command has expected output. No "TBD" or "add appropriate error handling".

### Type consistency
- `BackfillArticleRequestedByCommand.GroupId` (string) — Task 6 defines, Tasks 7/8/10/11 use consistently.
- `BackfillArticleRequestedByResponse` — fields `Total`, `AlreadyMigrated`, `Resolved`, `Ambiguous`, `Unresolved`, `WasDryRun`, `UnresolvedRows` — Tasks 6/7/8 align.
- `UnresolvedArticleRow` — fields `ArticleId`, `OriginalValue`, `Reason` — Tasks 6/7/8 align.
- `IArticleAdminRepository.ListWithRequestedByAsync` / `SaveChangesAsync` — Task 7 mocks it, Task 8 implements it.
- `currentUser.GetIdentifier()` extension — already exists (`CurrentUserExtensions.cs:16`); Tasks 2 & 4 use the same call site.
