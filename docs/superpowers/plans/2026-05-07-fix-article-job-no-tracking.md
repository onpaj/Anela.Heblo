# Fix: Article generation job silently drops all changes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix `GenerateArticleJob` so that article status, title, HTML content, and sources are actually persisted after a successful generation run instead of silently discarded.

**Architecture:** The job calls `GetByIdAsync` (which applies `AsNoTracking()` and returns a detached entity). All mutations are then ignored by EF Core's change tracker, making `SaveChangesAsync` a no-op. The fix is to call `GetForUpdateAsync` instead — that method already exists on the repository and returns a tracked entity. All tests that mock the repository also need their setup updated from `GetByIdAsync` to `GetForUpdateAsync`.

**Tech Stack:** .NET 9, EF Core, Hangfire, xUnit 2.9.2, Moq 4.20.72, FluentAssertions 6.12.0

---

### Task 1: Update existing tests to mock `GetForUpdateAsync` (TDD — make tests RED first)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Article/Pipeline/GenerateArticleJobTests.cs`

The four existing tests all mock `GetByIdAsync`. After this step they must FAIL because the mock returns null for the still-unset `GetForUpdateAsync` path, so the job logs "not found" and returns — status stays `Queued`.

- [ ] **Step 1: Replace all `GetByIdAsync` mock setups with `GetForUpdateAsync`**

In `GenerateArticleJobTests.cs`, replace every occurrence of:
```csharp
.Setup(r => r.GetByIdAsync(
```
with:
```csharp
.Setup(r => r.GetForUpdateAsync(
```

There are exactly 4 occurrences — lines 75, 111, 128, 162. The method signature is identical (`Guid id, CancellationToken ct`) so only the method name changes.

Also add a `Verify` at the end of `RunAsync_HappyPath_StatusGeneratedAndSourcesPersisted` to lock in the contract permanently:
```csharp
_repository.Verify(r => r.GetForUpdateAsync(article.Id, It.IsAny<CancellationToken>()), Times.Once);
_repository.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
```

After the edit the full test file should look like this:

```csharp
using Anela.Heblo.Application.Features.Article;
using Anela.Heblo.Application.Features.Article.UseCases.Generate;
using Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline;
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Application.Shared.WebSearch;
using Anela.Heblo.Domain.Features.Article;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using DomainArticle = Anela.Heblo.Domain.Features.Article.Article;

namespace Anela.Heblo.Tests.Article.Pipeline;

public class GenerateArticleJobTests
{
    private readonly Mock<IArticleRepository> _repository = new();
    private readonly Mock<IChatClient> _chat = new();
    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<IWebSearchClient> _webSearch = new();
    private readonly Mock<IOneDriveService> _oneDrive = new();
    private readonly ArticleOptions _options = new();

    private static DomainArticle CreateArticle() =>
        new()
        {
            Id = Guid.NewGuid(),
            Topic = "Topic",
            Status = ArticleStatus.Queued
        };

    private GenerateArticleJob CreateJob(
        PlanQueriesStep? planQueries = null,
        GatherContextStep? gatherContext = null,
        AggregateFactsStep? aggregateFacts = null,
        ValidateFactsStep? validateFacts = null,
        WriteArticleStep? writeArticle = null)
    {
        var optionsWrapper = Options.Create(_options);
        return new GenerateArticleJob(
            _repository.Object,
            planQueries ?? new PlanQueriesStep(_chat.Object, optionsWrapper, NullLogger<PlanQueriesStep>.Instance),
            gatherContext ?? new GatherContextStep(_mediator.Object, _webSearch.Object, _oneDrive.Object, optionsWrapper, NullLogger<GatherContextStep>.Instance),
            aggregateFacts ?? new AggregateFactsStep(_chat.Object, optionsWrapper, NullLogger<AggregateFactsStep>.Instance),
            validateFacts ?? new ValidateFactsStep(_chat.Object, optionsWrapper, NullLogger<ValidateFactsStep>.Instance),
            writeArticle ?? new WriteArticleStep(_chat.Object, optionsWrapper, NullLogger<WriteArticleStep>.Instance),
            NullLogger<GenerateArticleJob>.Instance);
    }

    private void SetupChatResponses(params string[] responsesInOrder)
    {
        var queue = new Queue<string>(responsesInOrder);
        _chat
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var text = queue.Count > 0 ? queue.Dequeue() : "{}";
                return new ChatResponse([new ChatMessage(ChatRole.Assistant, text)]);
            });
    }

    [Fact]
    public async Task RunAsync_HappyPath_StatusGeneratedAndSourcesPersisted()
    {
        var article = CreateArticle();
        article.UsedKnowledgeBase = false;
        article.UsedWebSearch = false;
        _repository
            .Setup(r => r.GetForUpdateAsync(article.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(article);

        SetupChatResponses(
            // PlanQueries
            """{"queries":["q1","q2"]}""",
            // AggregateFacts
            """{"facts":[{"claim":"Fact A","confidence":0.9,"source_url":null,"source_title":"S"}],"summary":"sum","gaps":null}""",
            // ValidateFacts
            """{"validated_facts":[{"fact":"Fact A","note":"good","reliable":true}]}""",
            // WriteArticle
            """{"article_title":"Final Title","article_html":"<article>x</article>","sources_used":[{"title":"Src","url":"https://a.com"}]}"""
        );

        await CreateJob().RunAsync(article.Id, default);

        article.Status.Should().Be(ArticleStatus.Generated);
        article.Title.Should().Be("Final Title");
        article.HtmlContent.Should().Be("<article>x</article>");
        article.Sources.Should().ContainSingle();
        article.Sources[0].Title.Should().Be("Src");
        article.Sources[0].Url.Should().Be("https://a.com");
        article.Sources[0].Type.Should().Be(SourceType.Web);
        article.Sources[0].ArticleId.Should().Be(article.Id);

        // SaveChangesAsync called: after Researching, after Writing, after final
        _repository.Verify(
            r => r.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.AtLeast(3));
        // Regression guard: must use the tracked variant, never the read-only one
        _repository.Verify(r => r.GetForUpdateAsync(article.Id, It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_ArticleNotFound_LogsAndReturnsWithoutSavingState()
    {
        var id = Guid.NewGuid();
        _repository
            .Setup(r => r.GetForUpdateAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DomainArticle?)null);

        await CreateJob().RunAsync(id, default);

        _repository.Verify(
            r => r.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunAsync_StepThrows_StatusFailedAndErrorMessageSet()
    {
        var article = CreateArticle();
        article.UsedKnowledgeBase = false;
        article.UsedWebSearch = false;
        _repository
            .Setup(r => r.GetForUpdateAsync(article.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(article);

        var callCount = 0;
        _chat
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    // PlanQueries succeeds with valid JSON
                    return new ChatResponse([new ChatMessage(ChatRole.Assistant, """{"queries":["q1"]}""")]);
                }
                // AggregateFacts fails
                throw new InvalidOperationException("LLM blew up");
            });

        await CreateJob().RunAsync(article.Id, default);

        article.Status.Should().Be(ArticleStatus.Failed);
        article.ErrorMessage.Should().Be("LLM blew up");
    }

    [Fact]
    public async Task RunAsync_OperationCancelled_StatusFailedAndExceptionRethrown()
    {
        var article = CreateArticle();
        article.UsedKnowledgeBase = false;
        article.UsedWebSearch = false;
        _repository
            .Setup(r => r.GetForUpdateAsync(article.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(article);

        _chat
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        Func<Task> act = () => CreateJob().RunAsync(article.Id, default);

        await act.Should().ThrowAsync<OperationCanceledException>();
        article.Status.Should().Be(ArticleStatus.Failed);
        article.ErrorMessage.Should().Be("Job cancelled.");
    }
}
```

- [ ] **Step 2: Run the tests to confirm they now FAIL**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GenerateArticleJobTests" \
  --no-build \
  2>&1 | tail -30
```

Expected: 3 tests FAIL (HappyPath, StepThrows, OperationCancelled — article is null so job returns early). ArticleNotFound test PASSES (expected null path is the same). The `GetByIdAsync Times.Never` verify in HappyPath will PASS trivially since nothing is called.

---

### Task 2: Fix the implementation — one line

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/GenerateArticleJob.cs:40`

- [ ] **Step 1: Change `GetByIdAsync` to `GetForUpdateAsync` on line 40**

Replace:
```csharp
var article = await _repository.GetByIdAsync(articleId, ct);
```
with:
```csharp
var article = await _repository.GetForUpdateAsync(articleId, ct);
```

No other changes anywhere — `GetForUpdateAsync` has an identical signature `Task<Article?> GetForUpdateAsync(Guid id, CancellationToken ct)` on `IArticleRepository`. The null-check on line 41 still applies correctly.

- [ ] **Step 2: Build to confirm no compilation errors**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj \
  --no-restore 2>&1 | tail -10
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Run the tests to confirm they all PASS**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GenerateArticleJobTests" \
  2>&1 | tail -20
```

Expected: 4/4 PASS

- [ ] **Step 4: Run the full Article test suite to catch any regressions**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Anela.Heblo.Tests.Article" \
  2>&1 | tail -20
```

Expected: All Article tests pass.

- [ ] **Step 5: Commit**

```bash
git add \
  backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/GenerateArticleJob.cs \
  backend/test/Anela.Heblo.Tests/Article/Pipeline/GenerateArticleJobTests.cs
git commit -m "fix: use tracked entity in GenerateArticleJob so EF persists status and sources"
```
