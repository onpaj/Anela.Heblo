## Module
Article

## Finding
`GenerateArticleJob` takes all five pipeline steps as concrete class dependencies rather than interfaces:

```csharp
// backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/GenerateArticleJob.cs:17–35
public GenerateArticleJob(
    IArticleRepository repository,
    PlanQueriesStep planQueries,       // concrete class
    GatherContextStep gatherContext,   // concrete class
    AggregateFactsStep aggregateFacts, // concrete class
    ValidateFactsStep validateFacts,   // concrete class
    WriteArticleStep writeArticle,     // concrete class
    ILogger logger)
```

None of the step classes have a matching interface, and `ExecuteAsync` on each step is neither `virtual` nor `abstract`. This means Moq (or any substitute-based test double) cannot intercept `ExecuteAsync`, so the method cannot be replaced with a test double at the type level.

This has a concrete cost in `GenerateArticleJobTests.cs` (lines 42–59): to test the job's orchestration logic (status transitions, error handling, source persistence), the tests must construct real `PlanQueriesStep`, `GatherContextStep`, etc. instances with mocked inner dependencies (`IChatClient`, `IArticleKnowledgeSource`, `IWebSearchClient`). For example, to test what happens when only `AggregateFactsStep` throws, the test must wire up a real `PlanQueriesStep` that successfully parses a valid JSON query plan from `_chat`, then arrange `_chat` to throw on the second call. The job's error-handling path cannot be tested independently of the steps' JSON parsing logic.

The DI registrations in `ArticleModule.cs` (lines 25–30) mirror the same pattern — each step is bound as a concrete type with no interface.

## Why it matters
Violates the Dependency Inversion Principle: the high-level orchestrator (`GenerateArticleJob`) depends on low-level concretions, not abstractions. Side-effects:

- **Testability**: the job's orchestration logic (status transitions, exception handling, `MarkAsFailed`/`MarkAsGenerated` calls) can only be exercised through the real step implementations. A targeted unit test for "job marks article as Failed when a step throws" requires a successfully-completed prior step, not just a stub that does nothing.
- **Replaceability**: swapping a step implementation (e.g. a `MockPlanQueriesStep` in a dev environment) requires changing both the DI registration *and* the constructor signature of `GenerateArticleJob`.

## Suggested fix
Introduce a minimal interface per step with a single method:

```csharp
public interface IPlanQueriesStep
{
    Task ExecuteAsync(ArticlePipelineContext context, CancellationToken ct);
}

public class PlanQueriesStep : IPlanQueriesStep { ... }
```

Apply the same pattern to `GatherContextStep`, `AggregateFactsStep`, `ValidateFactsStep`, and `WriteArticleStep`. Update `GenerateArticleJob`'s constructor and `ArticleModule.cs` DI bindings accordingly. The job tests can then use `Mock` to isolate each test case to exactly the orchestration behaviour under test.

---
_Filed by daily arch-review routine on 2026-07-11._
