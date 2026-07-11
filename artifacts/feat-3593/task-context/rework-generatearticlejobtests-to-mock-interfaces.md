### task: rework-generatearticlejobtests-to-mock-interfaces

**Files:**
- `backend/test/Anela.Heblo.Tests/Article/Pipeline/GenerateArticleJobTests.cs`

**Steps:**

- [ ] Replace the field declarations at the top of the class:
  ```csharp
  public class GenerateArticleJobTests
  {
      private readonly Mock<IArticleRepository> _repository = new();
      private readonly Mock<IChatClient> _chat = new();
      private readonly Mock<IArticleKnowledgeSource> _knowledgeSource = new();
      private readonly Mock<IWebSearchClient> _webSearch = new();
      private readonly Mock<IArticleStyleGuideSource> _styleGuideSource = new();
      private readonly ArticleOptions _options = new();
  ```
  with:
  ```csharp
  public class GenerateArticleJobTests
  {
      private readonly Mock<IArticleRepository> _repository = new();
      private readonly Mock<IPlanQueriesStep> _planQueries = new();
      private readonly Mock<IGatherContextStep> _gatherContext = new();
      private readonly Mock<IAggregateFactsStep> _aggregateFacts = new();
      private readonly Mock<IValidateFactsStep> _validateFacts = new();
      private readonly Mock<IWriteArticleStep> _writeArticle = new();
  ```
  (`ArticleOptions _options`, `IChatClient`, `IArticleKnowledgeSource`, `IWebSearchClient`, and `IArticleStyleGuideSource` mocks are dropped — they were only needed to construct real step instances, which no longer happens in this file.)

- [ ] Set up the five new step mocks as successful no-ops by default, right after the field declarations (before `CreateArticle`), by initializing them in the constructor. Add a constructor:
  ```csharp
  public GenerateArticleJobTests()
  {
      _planQueries.Setup(s => s.ExecuteAsync(It.IsAny<ArticlePipelineContext>(), It.IsAny<CancellationToken>()))
          .Returns(Task.CompletedTask);
      _gatherContext.Setup(s => s.ExecuteAsync(It.IsAny<ArticlePipelineContext>(), It.IsAny<CancellationToken>()))
          .Returns(Task.CompletedTask);
      _aggregateFacts.Setup(s => s.ExecuteAsync(It.IsAny<ArticlePipelineContext>(), It.IsAny<CancellationToken>()))
          .Returns(Task.CompletedTask);
      _validateFacts.Setup(s => s.ExecuteAsync(It.IsAny<ArticlePipelineContext>(), It.IsAny<CancellationToken>()))
          .Returns(Task.CompletedTask);
      _writeArticle.Setup(s => s.ExecuteAsync(It.IsAny<ArticlePipelineContext>(), It.IsAny<CancellationToken>()))
          .Returns(Task.CompletedTask);
  }
  ```
  Place this directly after the field declarations block, before `CreateArticle()`.

- [ ] Remove the `CreateNoOpRecorder()` helper entirely (it is only used to construct real step instances, which no longer happens):
  ```csharp
  private static PipelineStepRecorder CreateNoOpRecorder()
  {
      var repo = new Mock<IArticleRepository>();
      repo.Setup(r => r.AddStepAsync(It.IsAny<ArticleGenerationStep>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
      repo.Setup(r => r.UpdateStepAsync(It.IsAny<ArticleGenerationStep>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
      repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
      return new PipelineStepRecorder(repo.Object);
  }
  ```
  Delete this method entirely.

- [ ] Replace `CreateJob(...)`:
  ```csharp
  private GenerateArticleJob CreateJob(
      PlanQueriesStep? planQueries = null,
      GatherContextStep? gatherContext = null,
      AggregateFactsStep? aggregateFacts = null,
      ValidateFactsStep? validateFacts = null,
      WriteArticleStep? writeArticle = null)
  {
      var optionsWrapper = Options.Create(_options);
      var recorder = CreateNoOpRecorder();
      return new GenerateArticleJob(
          _repository.Object,
          planQueries ?? new PlanQueriesStep(_chat.Object, optionsWrapper, NullLogger<PlanQueriesStep>.Instance, recorder),
          gatherContext ?? new GatherContextStep(_knowledgeSource.Object, _webSearch.Object, _styleGuideSource.Object, optionsWrapper, NullLogger<GatherContextStep>.Instance, recorder),
          aggregateFacts ?? new AggregateFactsStep(_chat.Object, optionsWrapper, NullLogger<AggregateFactsStep>.Instance, recorder),
          validateFacts ?? new ValidateFactsStep(_chat.Object, optionsWrapper, NullLogger<ValidateFactsStep>.Instance, recorder),
          writeArticle ?? new WriteArticleStep(_chat.Object, optionsWrapper, NullLogger<WriteArticleStep>.Instance, recorder),
          NullLogger<GenerateArticleJob>.Instance);
  }
  ```
  with:
  ```csharp
  private GenerateArticleJob CreateJob()
  {
      return new GenerateArticleJob(
          _repository.Object,
          _planQueries.Object,
          _gatherContext.Object,
          _aggregateFacts.Object,
          _validateFacts.Object,
          _writeArticle.Object,
          NullLogger<GenerateArticleJob>.Instance);
  }
  ```

- [ ] Remove the `SetupChatResponses` helper entirely (no longer used by any test once steps are mocked directly):
  ```csharp
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
  ```
  Delete this method entirely.

- [ ] Rework `RunAsync_HappyPath_StatusGeneratedAndSourcesPersisted`. Replace:
  ```csharp
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
  ```
  with:
  ```csharp
  [Fact]
  public async Task RunAsync_HappyPath_StatusGeneratedAndSourcesPersisted()
  {
      var article = CreateArticle();
      article.UsedKnowledgeBase = false;
      article.UsedWebSearch = false;
      _repository
          .Setup(r => r.GetForUpdateAsync(article.Id, It.IsAny<CancellationToken>()))
          .ReturnsAsync(article);

      _writeArticle
          .Setup(s => s.ExecuteAsync(It.IsAny<ArticlePipelineContext>(), It.IsAny<CancellationToken>()))
          .Callback<ArticlePipelineContext, CancellationToken>((ctx, ct) =>
          {
              ctx.GeneratedTitle = "Final Title";
              ctx.GeneratedHtml = "<article>x</article>";
              ctx.SourceRefs = new List<ArticleSourceRef>
              {
                  new("Src", "https://a.com", SourceType.Web, null, null, null, null)
              };
          })
          .Returns(Task.CompletedTask);

      await CreateJob().RunAsync(article.Id, default);
  ```
  The rest of the test method (assertions from `article.Status.Should().Be(ArticleStatus.Generated);` through the end) is unchanged — the assertions read off `article`, which is populated the same way regardless of whether a real or mocked `WriteArticleStep` set `context.GeneratedTitle`/`GeneratedHtml`/`SourceRefs`.

- [ ] Rework `RunAsync_StepThrows_StatusFailedAndErrorMessageSet`. Replace:
  ```csharp
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
  ```
  with:
  ```csharp
  [Fact]
  public async Task RunAsync_StepThrows_StatusFailedAndErrorMessageSet()
  {
      var article = CreateArticle();
      article.UsedKnowledgeBase = false;
      article.UsedWebSearch = false;
      _repository
          .Setup(r => r.GetForUpdateAsync(article.Id, It.IsAny<CancellationToken>()))
          .ReturnsAsync(article);

      _aggregateFacts
          .Setup(s => s.ExecuteAsync(It.IsAny<ArticlePipelineContext>(), It.IsAny<CancellationToken>()))
          .ThrowsAsync(new InvalidOperationException("LLM blew up"));

      await CreateJob().RunAsync(article.Id, default);

      article.Status.Should().Be(ArticleStatus.Failed);
      article.ErrorMessage.Should().Be("LLM blew up");
  }
  ```
  (`_planQueries` stays a successful no-op via the constructor's default setup, so the job reaches `_aggregateFacts.ExecuteAsync` before failing — no `IChatClient`/JSON setup needed at all.)

- [ ] Rework `RunAsync_OperationCancelled_StatusFailedAndExceptionRethrown`. Replace:
  ```csharp
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
  ```
  with:
  ```csharp
  [Fact]
  public async Task RunAsync_OperationCancelled_StatusFailedAndExceptionRethrown()
  {
      var article = CreateArticle();
      article.UsedKnowledgeBase = false;
      article.UsedWebSearch = false;
      _repository
          .Setup(r => r.GetForUpdateAsync(article.Id, It.IsAny<CancellationToken>()))
          .ReturnsAsync(article);

      _planQueries
          .Setup(s => s.ExecuteAsync(It.IsAny<ArticlePipelineContext>(), It.IsAny<CancellationToken>()))
          .ThrowsAsync(new OperationCanceledException());

      Func<Task> act = () => CreateJob().RunAsync(article.Id, default);

      await act.Should().ThrowAsync<OperationCanceledException>();
      article.Status.Should().Be(ArticleStatus.Failed);
      article.ErrorMessage.Should().Be("Job cancelled.");
  }
  ```

- [ ] Leave `RunAsync_ArticleNotFound_LogsAndReturnsWithoutSavingState` as-is except for the already-updated `CreateJob()` signature (no args) — no other change needed since it returns before any step is invoked:
  ```csharp
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
  ```
  (This method already calls `CreateJob()` with no arguments, so no textual change is required here — confirm it still compiles against the new no-arg `CreateJob()` signature.)

- [ ] Update the `using` directives at the top of the file. Replace:
  ```csharp
  using Anela.Heblo.Application.Features.Article;
  using Anela.Heblo.Application.Features.Article.Contracts;
  using Anela.Heblo.Application.Features.Article.UseCases.Generate;
  using Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline;
  using Anela.Heblo.Application.Shared.WebSearch;
  using Anela.Heblo.Domain.Features.Article;
  using FluentAssertions;
  using Microsoft.Extensions.AI;
  using Microsoft.Extensions.Logging.Abstractions;
  using Microsoft.Extensions.Options;
  using Moq;
  using DomainArticle = Anela.Heblo.Domain.Features.Article.Article;
  ```
  with:
  ```csharp
  using Anela.Heblo.Application.Features.Article;
  using Anela.Heblo.Application.Features.Article.UseCases.Generate;
  using Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline;
  using Anela.Heblo.Domain.Features.Article;
  using FluentAssertions;
  using Microsoft.Extensions.Logging.Abstractions;
  using Moq;
  using DomainArticle = Anela.Heblo.Domain.Features.Article.Article;
  ```
  Rationale: `Anela.Heblo.Application.Features.Article.Contracts` (for `IArticleKnowledgeSource`/`IArticleStyleGuideSource`), `Anela.Heblo.Application.Shared.WebSearch` (for `IWebSearchClient`), `Microsoft.Extensions.AI` (for `IChatClient`/`ChatMessage`/`ChatResponse`/`ChatOptions`), and `Microsoft.Extensions.Options` (for `Options.Create`) are no longer referenced anywhere in the file after the above changes. Keep `Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline` — it's still needed for `ArticlePipelineContext`, `ArticleSourceRef`, and the five step interfaces.

- [ ] Build the test project and run the full `GenerateArticleJobTests` suite plus the rest of `Anela.Heblo.Tests`:
  ```bash
  cd backend
  dotnet build
  dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GenerateArticleJobTests"
  ```
  Expect all 4 tests to pass: `RunAsync_HappyPath_StatusGeneratedAndSourcesPersisted`, `RunAsync_ArticleNotFound_LogsAndReturnsWithoutSavingState`, `RunAsync_StepThrows_StatusFailedAndErrorMessageSet`, `RunAsync_OperationCancelled_StatusFailedAndExceptionRethrown`.

- [ ] Run the full backend test suite to confirm nothing else regressed (including the five per-step `*StepTests.cs` files and `SourceEnrichmentIntegrationTests.cs`, which construct the concrete step classes directly and must be unaffected):
  ```bash
  cd backend
  dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
  ```
  Expect all tests to pass with no new warnings about unused mocks/fields.

- [ ] Run `dotnet format` and confirm no diffs:
  ```bash
  cd backend
  dotnet format --verify-no-changes
  ```

- [ ] Commit:
  ```bash
  git add backend/test/Anela.Heblo.Tests/Article/Pipeline/GenerateArticleJobTests.cs
  git commit -m "Rework GenerateArticleJobTests to mock pipeline step interfaces directly"
  ```

---

## Self-Review Notes

- **Spec coverage:** FR-1 (interfaces defined, co-located, single method) → task 1. FR-2 (step classes implement interface, no other member changes) → task 1. FR-3 (`GenerateArticleJob` depends on interfaces, `RunAsync` body unchanged) → task 1. FR-4 (DI binds interface to implementation, `PipelineStepRecorder`/`GenerateArticleJob` registrations untouched) → task 1. FR-5 (test rework: mock interfaces, happy-path `WriteArticleStep` callback sets `context` fields, `StepThrows` mocks `IAggregateFactsStep` directly with `IPlanQueriesStep` as no-op, `OperationCancelled` mocks `IPlanQueriesStep` directly, `ArticleNotFound` unaffected, unused mocks/recorder removed) → task 2. NFR-1 (behavior parity, `RunAsync` body untouched) → enforced by task 1's exact-replacement instructions. NFR-2 (build/format/test integrity, repo-wide grep for other concrete-type consumers) → both tasks' verification steps. NFR-3 (no alternate implementations introduced) → task 1 registers exactly one implementation per interface, nothing more.
- **Placeholder scan:** No "TBD", "similar to above", or "add appropriate X" phrasing anywhere in the task steps above — every code block is the literal before/after text, every verification step has a runnable command with an expected outcome.
- **Type consistency:** `ArticleSourceRef` is a `sealed record` with constructor `(string Title, string? Url, SourceType Type, Guid? ChunkId, double? Confidence, string? Excerpt, string? ValidationNote)` (confirmed by reading `ArticleSourceRef.cs`) — the happy-path callback's positional-constructor call `new("Src", "https://a.com", SourceType.Web, null, null, null, null)` matches this signature exactly, and the resulting values match what the original JSON-driven test asserted (`Sources[0].Title == "Src"`, `.Url == "https://a.com"`, `.Type == SourceType.Web`). `ArticlePipelineContext.SourceRefs` already defaults to `List<ArticleSourceRef> = []` (confirmed by reading `ArticlePipelineContext.cs`), so the default no-op mocks used by the other three tests leave `SourceRefs` as an empty list, matching `RunAsync`'s `foreach (var sourceRef in context.SourceRefs)` with zero iterations — no null-reference risk.
- **DI/build ordering:** Task 1 is verified with `dotnet build` alone (not `dotnet test`) because at that point `GenerateArticleJobTests.cs` still constructs real step instances via `new PlanQueriesStep(...)` etc. passed into interface-typed constructor parameters — this compiles (each concrete class now implements its interface) even though the test file hasn't been rewritten yet. Task 2 is the only task that changes test behavior/assertions, so it carries the `dotnet test` verification.
- **Execution handoff:** Skipped — this plan is consumed by an automated pipeline with no human in the loop.
