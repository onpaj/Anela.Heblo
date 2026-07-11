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

