## Module
Article

## Finding
`IArticlePipelineStep.cs` defines:

```csharp
public interface IArticlePipelineStep
{
    Task ExecuteAsync(ArticlePipelineContext context, CancellationToken ct);
}
```

All five pipeline step classes implement it (`PlanQueriesStep`, `GatherContextStep`, `AggregateFactsStep`, `ValidateFactsStep`, `WriteArticleStep`), but:

1. `GenerateArticleJob.cs:14-18` injects the five concrete types directly — not via `IArticlePipelineStep`.
2. `ArticleModule.cs:21-25` registers each step as its concrete type — not by interface.

No code anywhere resolves or injects `IArticlePipelineStep`.

## Why it matters
- The interface is dead code that adds false signal: a reader expects the abstraction to be used somewhere for polymorphism or testability, but it is not.
- `GenerateArticleJob` tests (when written) must mock the five concrete types, not a single interface, giving up the benefit the interface was presumably created for.
- YAGNI: the sequential, fixed pipeline does not need runtime-swappable steps; if it ever does, the interface can be introduced then.

## Suggested fix
Two valid options:

**Option A — Remove the interface** (smallest change, honest about current use):
Delete `IArticlePipelineStep.cs`. Each step remains a plain class. The pipeline is hard-coded and that is fine for now.

**Option B — Actually use the interface** (if testability is the motivation):
Register each step by interface in `ArticleModule.cs` and inject `IEnumerable<IArticlePipelineStep>` (or individual named registrations) in `GenerateArticleJob`. Tests can then mock `IArticlePipelineStep` instead of concrete classes.

Option A is recommended unless there is a specific plan to add runtime step composition.

---
_Filed by daily arch-review routine on 2026-05-25._