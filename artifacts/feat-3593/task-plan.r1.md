# Implementation Plan: Introduce interfaces for Article generation pipeline steps

## Context

`GenerateArticleJob` (`backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/GenerateArticleJob.cs`) currently takes the five pipeline step classes (`PlanQueriesStep`, `GatherContextStep`, `AggregateFactsStep`, `ValidateFactsStep`, `WriteArticleStep`) as concrete-type constructor dependencies. None of these classes expose a mockable seam, so `GenerateArticleJobTests.cs` has to build real step instances (with mocked `IChatClient`/`IArticleKnowledgeSource`/`IWebSearchClient`/`IArticleStyleGuideSource`/`PipelineStepRecorder`) even for tests that only care about the job's own orchestration logic.

This plan:
1. Adds one single-method interface per step (co-located in the step's own file), makes each step class implement it, and switches `GenerateArticleJob` + `ArticleModule.cs` DI registrations to depend on the interfaces.
2. Reworks `GenerateArticleJobTests.cs` to mock the five interfaces directly instead of constructing real step instances.

This is a pure Dependency Inversion refactor — no behavior, business logic, or `RunAsync` method-body changes (only field/parameter *types* change). See `spec.r1.md` (FR-1 through FR-5, NFR-1 through NFR-3), `arch-review.r1.md`, and `design.r1.md` for the full rationale — no deviations from those documents are introduced here.

## Task Overview

- `task: add-pipeline-step-interfaces-and-wire-di` — define the five interfaces, implement them on the step classes, update `GenerateArticleJob`'s constructor/fields and `ArticleModule.cs`'s DI registrations.
- `task: rework-generatearticlejobtests-to-mock-interfaces` — rework `GenerateArticleJobTests.cs` to mock the five interfaces directly, dropping the now-unneeded leaf-dependency mocks and no-op recorder.

Each task is independently committable and verifiable via `dotnet build` (task 1) and `dotnet build` + `dotnet test` (task 2).

---

### task: add-pipeline-step-interfaces-and-wire-di

**Files:**
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/PlanQueriesStep.cs`
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/GatherContextStep.cs`
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/AggregateFactsStep.cs`
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/ValidateFactsStep.cs`
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/WriteArticleStep.cs`
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/GenerateArticleJob.cs`
- `backend/src/Anela.Heblo.Application/Features/Article/ArticleModule.cs`

**Steps:**

- [ ] In `PlanQueriesStep.cs`, replace:
  ```csharp
  namespace Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline;

  public class PlanQueriesStep
  {
  ```
  with:
  ```csharp
  namespace Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline;

  public interface IPlanQueriesStep
  {
      Task ExecuteAsync(ArticlePipelineContext context, CancellationToken ct);
  }

  public class PlanQueriesStep : IPlanQueriesStep
  {
  ```

- [ ] In `GatherContextStep.cs`, replace:
  ```csharp
  namespace Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline;

  public class GatherContextStep
  {
  ```
  with:
  ```csharp
  namespace Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline;

  public interface IGatherContextStep
  {
      Task ExecuteAsync(ArticlePipelineContext context, CancellationToken ct);
  }

  public class GatherContextStep : IGatherContextStep
  {
  ```

- [ ] In `AggregateFactsStep.cs`, replace:
  ```csharp
  namespace Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline;

  public class AggregateFactsStep
  {
  ```
  with:
  ```csharp
  namespace Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline;

  public interface IAggregateFactsStep
  {
      Task ExecuteAsync(ArticlePipelineContext context, CancellationToken ct);
  }

  public class AggregateFactsStep : IAggregateFactsStep
  {
  ```

- [ ] In `ValidateFactsStep.cs`, replace:
  ```csharp
  namespace Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline;

  public class ValidateFactsStep
  {
  ```
  with:
  ```csharp
  namespace Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline;

  public interface IValidateFactsStep
  {
      Task ExecuteAsync(ArticlePipelineContext context, CancellationToken ct);
  }

  public class ValidateFactsStep : IValidateFactsStep
  {
  ```

- [ ] In `WriteArticleStep.cs`, replace:
  ```csharp
  namespace Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline;

  public class WriteArticleStep
  {
  ```
  with:
  ```csharp
  namespace Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline;

  public interface IWriteArticleStep
  {
      Task ExecuteAsync(ArticlePipelineContext context, CancellationToken ct);
  }

  public class WriteArticleStep : IWriteArticleStep
  {
  ```

- [ ] Verify each step class's own `ExecuteAsync` method already matches the interface signature exactly (`Task ExecuteAsync(ArticlePipelineContext context, CancellationToken ct)`) so no further changes to the class bodies are needed. (Confirmed by inspection during planning — all five already use this exact signature; this is a read-only sanity check, not an edit.)

- [ ] In `GenerateArticleJob.cs`, replace the field declarations:
  ```csharp
  private readonly IArticleRepository _repository;
  private readonly PlanQueriesStep _planQueries;
  private readonly GatherContextStep _gatherContext;
  private readonly AggregateFactsStep _aggregateFacts;
  private readonly ValidateFactsStep _validateFacts;
  private readonly WriteArticleStep _writeArticle;
  private readonly ILogger<GenerateArticleJob> _logger;

  public GenerateArticleJob(
      IArticleRepository repository,
      PlanQueriesStep planQueries,
      GatherContextStep gatherContext,
      AggregateFactsStep aggregateFacts,
      ValidateFactsStep validateFacts,
      WriteArticleStep writeArticle,
      ILogger<GenerateArticleJob> logger)
  {
  ```
  with:
  ```csharp
  private readonly IArticleRepository _repository;
  private readonly IPlanQueriesStep _planQueries;
  private readonly IGatherContextStep _gatherContext;
  private readonly IAggregateFactsStep _aggregateFacts;
  private readonly IValidateFactsStep _validateFacts;
  private readonly IWriteArticleStep _writeArticle;
  private readonly ILogger<GenerateArticleJob> _logger;

  public GenerateArticleJob(
      IArticleRepository repository,
      IPlanQueriesStep planQueries,
      IGatherContextStep gatherContext,
      IAggregateFactsStep aggregateFacts,
      IValidateFactsStep validateFacts,
      IWriteArticleStep writeArticle,
      ILogger<GenerateArticleJob> logger)
  {
  ```
  Do not touch the constructor body (`_repository = repository; ...`) or `RunAsync` — they are unchanged (field names, assignment order, and all orchestration logic stay byte-for-byte identical; only the declared types above change).

- [ ] In `ArticleModule.cs`, replace:
  ```csharp
  services.AddScoped<PipelineStepRecorder>();
  services.AddScoped<PlanQueriesStep>();
  services.AddScoped<GatherContextStep>();
  services.AddScoped<AggregateFactsStep>();
  services.AddScoped<ValidateFactsStep>();
  services.AddScoped<WriteArticleStep>();
  services.AddScoped<GenerateArticleJob>();
  ```
  with:
  ```csharp
  services.AddScoped<PipelineStepRecorder>();
  services.AddScoped<IPlanQueriesStep, PlanQueriesStep>();
  services.AddScoped<IGatherContextStep, GatherContextStep>();
  services.AddScoped<IAggregateFactsStep, AggregateFactsStep>();
  services.AddScoped<IValidateFactsStep, ValidateFactsStep>();
  services.AddScoped<IWriteArticleStep, WriteArticleStep>();
  services.AddScoped<GenerateArticleJob>();
  ```

- [ ] Search the repo for any other direct references to the five concrete step type names outside the files already touched in this task and the test files that will be handled in the next task, to confirm nothing else depends on the concrete type in a way this refactor would break:
  ```bash
  cd backend
  grep -rn "PlanQueriesStep\|GatherContextStep\|AggregateFactsStep\|ValidateFactsStep\|WriteArticleStep" --include=*.cs src/ test/ | grep -v "UseCases/Generate/Pipeline/PlanQueriesStep.cs\|UseCases/Generate/Pipeline/GatherContextStep.cs\|UseCases/Generate/Pipeline/AggregateFactsStep.cs\|UseCases/Generate/Pipeline/ValidateFactsStep.cs\|UseCases/Generate/Pipeline/WriteArticleStep.cs\|UseCases/Generate/GenerateArticleJob.cs\|Features/Article/ArticleModule.cs\|GenerateArticleJobTests.cs"
  ```
  Expected result: only the five per-step test files (`PlanQueriesStepTests.cs`, `GatherContextStepTests.cs`, `AggregateFactsStepTests.cs`, `ValidateFactsStepTests.cs`, `WriteArticleStepTests.cs`) and `SourceEnrichmentIntegrationTests.cs`, all of which construct the concrete class directly via `new XStep(...)` — this remains valid since implementing an interface is purely additive, so no changes are needed to those files. If any other file appears, stop and investigate before proceeding — that would indicate an undocumented consumer not accounted for in `arch-review.r1.md`.

- [ ] Build the backend to confirm the interface introduction and DI rewiring compile cleanly:
  ```bash
  cd backend
  dotnet build
  ```
  Expect a clean build with no errors. (`GenerateArticleJobTests.cs` will still reference the concrete step types in `CreateJob(...)` at this point — that continues to compile because the concrete classes still exist unchanged and their constructors are untouched; only the `GenerateArticleJob` constructor call inside `CreateJob` now binds concrete instances to interface-typed parameters, which is valid since each concrete class implements its interface.)

- [ ] Run `dotnet format` and confirm no diffs:
  ```bash
  cd backend
  dotnet format --verify-no-changes
  ```

- [ ] Commit:
  ```bash
  git add backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/PlanQueriesStep.cs \
          backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/GatherContextStep.cs \
          backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/AggregateFactsStep.cs \
          backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/ValidateFactsStep.cs \
          backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/WriteArticleStep.cs \
          backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/GenerateArticleJob.cs \
          backend/src/Anela.Heblo.Application/Features/Article/ArticleModule.cs
  git commit -m "Introduce per-step interfaces for Article generation pipeline and wire DI"
  ```

---

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
