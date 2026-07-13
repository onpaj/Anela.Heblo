# Specification: Introduce interfaces for Article generation pipeline steps

## Summary
`GenerateArticleJob` currently depends on five concrete pipeline step classes (`PlanQueriesStep`, `GatherContextStep`, `AggregateFactsStep`, `ValidateFactsStep`, `WriteArticleStep`) instead of abstractions, and none of those classes expose a mockable, non-virtual `ExecuteAsync`. This forces `GenerateArticleJobTests` to construct real step instances (with mocked inner dependencies) even when a test only cares about the job's own orchestration logic. This spec introduces a minimal interface per step, updates the job and DI registrations to depend on those interfaces, and simplifies the existing job tests to use interface mocks.

## Background
`GenerateArticleJob.RunAsync` (`backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/GenerateArticleJob.cs`) orchestrates a fixed five-step pipeline against a shared `ArticlePipelineContext`: `PlanQueriesStep` → `GatherContextStep` → `AggregateFactsStep` → `ValidateFactsStep` → (status transition to `Writing`) → `WriteArticleStep`. Each step's constructor dependency in `GenerateArticleJob` is the concrete class, and `ExecuteAsync` is a plain (non-virtual) instance method, so Moq cannot generate a proxy for it — there is no seam to substitute a step with a test double.

`GenerateArticleJobTests.cs` (`backend/test/Anela.Heblo.Tests/Article/Pipeline/GenerateArticleJobTests.cs`) works around this today by building real step instances via `CreateJob(...)`, wired with mocked leaf dependencies (`Mock<IChatClient>`, `Mock<IArticleKnowledgeSource>`, `Mock<IWebSearchClient>`, `Mock<IArticleStyleGuideSource>`) and a real `PipelineStepRecorder` backed by a no-op repository mock. To test "job marks article as `Failed` when a step throws" (`RunAsync_StepThrows_StatusFailedAndErrorMessageSet`), the test must first make `PlanQueriesStep` succeed by returning valid JSON from the chat mock, then make the second chat call throw so `AggregateFactsStep` fails — coupling a job-orchestration test to `PlanQueriesStep`'s JSON-parsing behavior.

This is a pure Dependency Inversion refactor: introduce one interface per step, make each step class implement it, and change `GenerateArticleJob` and the DI container (`ArticleModule.cs`) to depend on the interfaces. No pipeline business logic changes.

## Functional Requirements

### FR-1: Define one interface per pipeline step
For each of the five step classes, define a matching interface with a single method, following the pattern from the brief:

```csharp
public interface IPlanQueriesStep
{
    Task ExecuteAsync(ArticlePipelineContext context, CancellationToken ct);
}
```

Required interfaces (method signature identical for all — `Task ExecuteAsync(ArticlePipelineContext context, CancellationToken ct)`):
- `IPlanQueriesStep` (for `PlanQueriesStep`)
- `IGatherContextStep` (for `GatherContextStep`)
- `IAggregateFactsStep` (for `AggregateFactsStep`)
- `IValidateFactsStep` (for `ValidateFactsStep`)
- `IWriteArticleStep` (for `WriteArticleStep`)

**Acceptance criteria:**
- Each interface lives in `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/`, in the same file as its implementing class (matching the brief's example), in namespace `Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline`.
- Each interface declares exactly one method: `Task ExecuteAsync(ArticlePipelineContext context, CancellationToken ct)`.
- No other members (properties, additional methods) are added to the interfaces.

### FR-2: Step classes implement their interface
Each of the five step classes declares itself as implementing the matching interface (e.g. `public class PlanQueriesStep : IPlanQueriesStep`). No change to constructor parameters, field layout, or method bodies of the step classes — this is a signature-only addition.

**Acceptance criteria:**
- `PlanQueriesStep : IPlanQueriesStep`, `GatherContextStep : IGatherContextStep`, `AggregateFactsStep : IAggregateFactsStep`, `ValidateFactsStep : IValidateFactsStep`, `WriteArticleStep : IWriteArticleStep`.
- `ExecuteAsync` on each class keeps its current implementation (JSON parsing, retry logic, recorder wrapping, fallback behavior) unchanged.
- No other public or private members of the step classes change.

### FR-3: `GenerateArticleJob` depends on the step interfaces, not the concrete classes
Update `GenerateArticleJob`'s constructor and private fields to use `IPlanQueriesStep`, `IGatherContextStep`, `IAggregateFactsStep`, `IValidateFactsStep`, `IWriteArticleStep` in place of the concrete class types. `RunAsync`'s orchestration logic (call order, status transitions, exception handling, `Source` mapping) is unchanged.

**Acceptance criteria:**
- Constructor signature parameter types are the five interfaces (order and names otherwise unchanged) plus the existing `IArticleRepository` and `ILogger<GenerateArticleJob>` parameters.
- `RunAsync` method body is byte-for-byte unchanged (only field/parameter *types* change).
- The class still compiles as `[AutomaticRetry(Attempts = 0)]` `public sealed class GenerateArticleJob`.

### FR-4: DI registrations bind interface to implementation
Update `ArticleModule.cs` (`backend/src/Anela.Heblo.Application/Features/Article/ArticleModule.cs`, lines 25–30) so each step is registered as `services.AddScoped<IXStep, XStep>()` instead of `services.AddScoped<XStep>()`.

**Acceptance criteria:**
- `services.AddScoped<IPlanQueriesStep, PlanQueriesStep>();`
- `services.AddScoped<IGatherContextStep, GatherContextStep>();`
- `services.AddScoped<IAggregateFactsStep, AggregateFactsStep>();`
- `services.AddScoped<IValidateFactsStep, ValidateFactsStep>();`
- `services.AddScoped<IWriteArticleStep, WriteArticleStep>();`
- `PipelineStepRecorder` and `GenerateArticleJob` registrations are unchanged (`services.AddScoped<PipelineStepRecorder>();` and `services.AddScoped<GenerateArticleJob>();`).
- The app builds and resolves `GenerateArticleJob` from the DI container without runtime resolution errors (verified via existing integration/build checks — Hangfire resolves job instances through the same container).

### FR-5: Simplify `GenerateArticleJobTests` to mock the step interfaces directly
Rework `GenerateArticleJobTests.cs` so `CreateJob(...)` accepts `Mock<IPlanQueriesStep>`, `Mock<IGatherContextStep>`, `Mock<IAggregateFactsStep>`, `Mock<IValidateFactsStep>`, `Mock<IWriteArticleStep>` (or their `.Object`s) instead of constructing real step instances. Default mocks should behave as successful no-ops (`ExecuteAsync` completes without mutating `context`, unless a test needs to mutate context fields the job reads afterward — see below).

Specifically:
- `RunAsync_HappyPath_StatusGeneratedAndSourcesPersisted` needs the `WriteArticleStep` mock to set `context.GeneratedTitle`, `context.GeneratedHtml`, and `context.SourceRefs` (since the job reads these directly off `context` after calling the step) — do this via `Mock<IWriteArticleStep>.Setup(...).Callback<ArticlePipelineContext, CancellationToken>((ctx, ct) => { ctx.GeneratedTitle = ...; ... })` or equivalent, rather than relying on real `WriteArticleStep` JSON parsing.
- `RunAsync_StepThrows_StatusFailedAndErrorMessageSet` should mock `IAggregateFactsStep.ExecuteAsync` to throw `InvalidOperationException("LLM blew up")` directly — no `IChatClient`/JSON setup needed at all. `_chat`, `_knowledgeSource`, `_webSearch`, `_styleGuideSource` mocks (and their setups) can be removed from the test class entirely once no test still exercises the real step implementations. Confirm no other test in the file (or the pipeline step unit tests, if any exist) still needs them before removing.
- `RunAsync_OperationCancelled_StatusFailedAndExceptionRethrown` should mock the first-invoked step (`IPlanQueriesStep`) to throw `OperationCanceledException` directly.
- `RunAsync_ArticleNotFound_LogsAndReturnsWithoutSavingState` is unaffected by which step type is used since no step is invoked; simplify its `CreateJob()` call along with the others.
- `PipelineStepRecorder` and its no-op repository wiring (`CreateNoOpRecorder`) are no longer needed by `GenerateArticleJobTests` once steps are mocked at the interface level (the recorder is an internal collaborator of the concrete step classes, not of the job) — remove it if no longer referenced.

**Acceptance criteria:**
- All four existing test methods pass with unchanged assertions (status, title, HTML, sources, `SaveChangesAsync` call counts, `ErrorMessage`).
- No test in `GenerateArticleJobTests.cs` constructs a real `PlanQueriesStep`, `GatherContextStep`, `AggregateFactsStep`, `ValidateFactsStep`, or `WriteArticleStep`.
- `RunAsync_StepThrows_StatusFailedAndErrorMessageSet` no longer depends on `PlanQueriesStep`'s JSON query-plan parsing to reach the `AggregateFactsStep` failure — it mocks `IAggregateFactsStep` to throw directly, with `IPlanQueriesStep` mocked as a successful no-op.
- If `Mock<IChatClient>`, `Mock<IArticleKnowledgeSource>`, `Mock<IWebSearchClient>`, `Mock<IArticleStyleGuideSource>` become unused after this change, they are removed from the test class along with their `using` statements if applicable.
- `dotnet test` for the `Anela.Heblo.Tests` project passes with no new warnings about unused mocks/fields.

## Non-Functional Requirements

### NFR-1: Behavior parity
This is a pure refactor. No change to article generation behavior, prompts, retry logic, JSON parsing, fallback handling, or persisted data. `RunAsync`'s method body must not change beyond field/parameter type annotations.

### NFR-2: Build and test integrity
- `dotnet build` succeeds for the whole solution (interfaces must not break any other consumer of the step classes — a repo-wide search for direct references to the five concrete step type names outside `ArticleModule.cs`, `GenerateArticleJob.cs`, and their own files should be done before finalizing the change, in case something elsewhere depends on the concrete type rather than the interface).
- `dotnet format` produces no diffs.
- All existing tests in `Anela.Heblo.Tests` (not just `GenerateArticleJobTests`) continue to pass, including any tests that directly exercise `PlanQueriesStep`, `GatherContextStep`, `AggregateFactsStep`, `ValidateFactsStep`, or `WriteArticleStep` in isolation (these should continue instantiating the concrete classes directly — introducing the interface does not require changing tests that already target the concrete step in isolation).

### NFR-3: No behavioral coupling to test doubles in production
The new interfaces are used only for the production DI graph (`ArticleModule.cs`) and are implemented by exactly one class each in production code. No additional "null object" or alternate implementation is introduced by this change (e.g. no `MockPlanQueriesStep` for dev environments — that is out of scope, see below).

## Data Model
No data model changes. `ArticlePipelineContext` (`backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/ArticlePipelineContext.cs`) remains the shared mutable state object passed by reference through each step's `ExecuteAsync`; its shape is unchanged.

## API / Interface Design

New interfaces (all in namespace `Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline`):

```csharp
public interface IPlanQueriesStep
{
    Task ExecuteAsync(ArticlePipelineContext context, CancellationToken ct);
}

public interface IGatherContextStep
{
    Task ExecuteAsync(ArticlePipelineContext context, CancellationToken ct);
}

public interface IAggregateFactsStep
{
    Task ExecuteAsync(ArticlePipelineContext context, CancellationToken ct);
}

public interface IValidateFactsStep
{
    Task ExecuteAsync(ArticlePipelineContext context, CancellationToken ct);
}

public interface IWriteArticleStep
{
    Task ExecuteAsync(ArticlePipelineContext context, CancellationToken ct);
}
```

`GenerateArticleJob` constructor signature after the change:

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

No HTTP/controller/UI-facing API surface changes — this is entirely internal to the Article module's Application layer.

## Dependencies
- Existing: Moq (already used in `GenerateArticleJobTests.cs`), Microsoft.Extensions.DependencyInjection (`ArticleModule.cs`), Hangfire (`GenerateArticleJob` is invoked as a Hangfire job — must remain resolvable from the DI container; no interface-based change affects Hangfire's job activation since it resolves `GenerateArticleJob` itself, not the interfaces, from the container).
- No new packages or external services introduced.

## Out of Scope
- Adding an interface for `PipelineStepRecorder` — it is an internal collaborator of each step, not a dependency of `GenerateArticleJob`, and is unaffected by this change.
- Any new alternate step implementation (e.g. a "mock"/no-op step for dev/staging environments) — the brief mentions this as a downstream benefit, not a requirement of this change.
- Any change to step internals: JSON parsing, retry logic (`ChatRetry`), prompt construction, fallback/rescue logic, or the `ArticlePipelineContext` shape.
- Adding unit tests for the individual step classes' internal logic beyond what already exists.
- Splitting the interface definitions into separate files from their implementing classes (see Open Questions — resolved via assumption, not a blocker).

## Open Questions
None.

## Status: COMPLETE
