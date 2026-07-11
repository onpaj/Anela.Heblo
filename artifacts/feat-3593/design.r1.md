# Design: Introduce interfaces for Article generation pipeline steps

## Component Design

This is a Dependency Inversion refactor confined to the `Article` module's Application layer. No new components are introduced; five existing pipeline step classes each gain a matching single-method interface, and their two consumers (`GenerateArticleJob` and `ArticleModule` DI registration) are updated to depend on the interfaces instead of the concrete classes.

### Pipeline step interfaces

Each of the five pipeline steps gets one interface, declared in the same file as its implementing class, in namespace `Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline`:

| Interface | Implementation | File |
|---|---|---|
| `IPlanQueriesStep` | `PlanQueriesStep` | `.../Pipeline/PlanQueriesStep.cs` |
| `IGatherContextStep` | `GatherContextStep` | `.../Pipeline/GatherContextStep.cs` |
| `IAggregateFactsStep` | `AggregateFactsStep` | `.../Pipeline/AggregateFactsStep.cs` |
| `IValidateFactsStep` | `ValidateFactsStep` | `.../Pipeline/ValidateFactsStep.cs` |
| `IWriteArticleStep` | `WriteArticleStep` | `.../Pipeline/WriteArticleStep.cs` |

Each interface exposes exactly one method:

```csharp
Task ExecuteAsync(ArticlePipelineContext context, CancellationToken ct);
```

Design intent (see `arch-review.r1.md` Decision 1): five distinct interfaces rather than one shared `IPipelineStep`, because `GenerateArticleJob`'s constructor pins each step to a fixed position in the pipeline (plan → gather → aggregate → validate → write). Distinct interfaces keep constructor-parameter-to-role mapping compiler-checked and keep `AddScoped<TInterface, TImpl>()` registrations unambiguous — a shared interface would require keyed/collection-based DI resolution for no benefit, since each interface has exactly one implementation and one call site.

Each interface is a narrow, module-internal test seam — it does **not** belong in `Features/Article/Contracts/` (reserved for cross-module/cross-use-case contracts per `development_guidelines.md`). No properties or additional methods are added beyond `ExecuteAsync`.

### `GenerateArticleJob`

Responsibility unchanged: orchestrates the fixed five-step pipeline against a shared `ArticlePipelineContext`, transitions article status (including to `Writing` before the write step), persists results, and maps failures to `Failed` status with `ErrorMessage`.

Only the constructor's dependency *types* change — from concrete classes to interfaces — plus the corresponding private field types. Constructor parameter order/names, and the entire `RunAsync` method body (call order, status transitions, exception handling, `Source` mapping), are unchanged:

```csharp
public GenerateArticleJob(
    IArticleRepository repository,
    IPlanQueriesStep planQueries,
    IGatherContextStep gatherContext,
    IAggregateFactsStep aggregateFacts,
    IValidateFactsStep validateFacts,
    IWriteArticleStep writeArticle,
    ILogger<GenerateArticleJob> logger)
```

Class remains `[AutomaticRetry(Attempts = 0)] public sealed class GenerateArticleJob`, still resolved by Hangfire from the DI container by its own concrete type (Hangfire never touches the step interfaces directly — confirmed in arch review via `GenerateArticleHandler.cs`'s `_backgroundJobClient.Enqueue<GenerateArticleJob>(...)`).

### `ArticleModule` (DI wiring)

Each step's registration changes from self-binding to interface binding:

```csharp
services.AddScoped<IPlanQueriesStep, PlanQueriesStep>();
services.AddScoped<IGatherContextStep, GatherContextStep>();
services.AddScoped<IAggregateFactsStep, AggregateFactsStep>();
services.AddScoped<IValidateFactsStep, ValidateFactsStep>();
services.AddScoped<IWriteArticleStep, WriteArticleStep>();
```

`PipelineStepRecorder` and `GenerateArticleJob` registrations are unchanged. `PipelineStepRecorder` stays a concrete, internal collaborator of each step class — it is not a dependency of `GenerateArticleJob` and is explicitly out of scope for interface extraction.

### `GenerateArticleJobTests`

Test-only component change: `CreateJob(...)` is reworked to accept `Mock<IPlanQueriesStep>`, `Mock<IGatherContextStep>`, `Mock<IAggregateFactsStep>`, `Mock<IValidateFactsStep>`, `Mock<IWriteArticleStep>` (or their `.Object`s), replacing construction of real step instances wired with mocked leaf dependencies (`IChatClient`, `IArticleKnowledgeSource`, `IWebSearchClient`, `IArticleStyleGuideSource`) and the no-op `PipelineStepRecorder`.

Per-test mocking behavior:
- Default mocks are successful no-ops (`ExecuteAsync` completes without mutating `context`).
- `RunAsync_HappyPath_StatusGeneratedAndSourcesPersisted`: `Mock<IWriteArticleStep>` is set up with a `Callback<ArticlePipelineContext, CancellationToken>` that sets `context.GeneratedTitle`, `context.GeneratedHtml`, and `context.SourceRefs` (initialized to a concrete list, not left null/default) so the job's post-call reads off `context` behave as today.
- `RunAsync_StepThrows_StatusFailedAndErrorMessageSet`: `Mock<IAggregateFactsStep>.Setup(...).ThrowsAsync(new InvalidOperationException("LLM blew up"))`, with `IPlanQueriesStep` mocked as a successful no-op — no chat/JSON setup required.
- `RunAsync_OperationCancelled_StatusFailedAndExceptionRethrown`: `Mock<IPlanQueriesStep>` (first-invoked step) throws `OperationCanceledException` directly.
- `RunAsync_ArticleNotFound_LogsAndReturnsWithoutSavingState`: unaffected by step type; `CreateJob()` call simplified along with the others.

Once no test in the file exercises a real step implementation, `Mock<IChatClient>`, `Mock<IArticleKnowledgeSource>`, `Mock<IWebSearchClient>`, `Mock<IArticleStyleGuideSource>`, and `CreateNoOpRecorder()`/`PipelineStepRecorder` wiring are removed along with unused `using` statements. The five per-step `*StepTests.cs` files and `SourceEnrichmentIntegrationTests.cs` are untouched — they instantiate the concrete step classes directly, which remains valid since implementing an interface is additive.

## Data Schemas

No data model, database, HTTP/controller, or persisted-payload changes. `ArticlePipelineContext` (`.../Pipeline/ArticlePipelineContext.cs`) keeps its existing shape and continues to flow by reference through each step's `ExecuteAsync` in the same fixed order:

```
PlanQueries → GatherContext → AggregateFacts → ValidateFacts → [status: Writing] → WriteArticle
```

Only the compile-time dependency type at the `GenerateArticleJob` constructor boundary changes (concrete class → interface); no wire format, event payload, or entity schema is affected.
