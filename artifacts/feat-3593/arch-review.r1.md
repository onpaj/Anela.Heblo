# Architecture Review: Introduce interfaces for Article generation pipeline steps

## Skip Design: true

## Architectural Fit Assessment

This is a textbook Dependency Inversion refactor, entirely internal to the Application layer of one module (`Article`). It touches no contracts, no controllers, no DTOs, and no cross-module boundaries, so it aligns cleanly with the Vertical Slice conventions in `docs/architecture/development_guidelines.md` and `docs/architecture/filesystem.md`:

- The five step classes already live under `Features/Article/UseCases/Generate/Pipeline/`, which is the correct home for use-case-internal collaborators (not `Services/`, since these are single-purpose pipeline stages, not feature-wide services).
- `ArticleModule.cs` is the correct — and only — place for the DI binding change (ADR-004: bindings live in the owning module, and this isn't even a repository binding, so ADR-004 doesn't strictly apply, but the same "one module owns its own wiring" principle holds).
- Confirmed via `grep` that the only consumer of `GenerateArticleJob` outside its own file is `GenerateArticleHandler.cs`, which enqueues it as `_backgroundJobClient.Enqueue<GenerateArticleJob>(...)`. Hangfire resolves `GenerateArticleJob` itself from the container and injects its constructor dependencies — it never touches the step types directly. Registering the five steps as `AddScoped<IXStep, XStep>()` instead of `AddScoped<XStep>()` is fully transparent to Hangfire's job activation.
- Confirmed via `grep` that `PlanQueriesStep`, `GatherContextStep`, `AggregateFactsStep`, `ValidateFactsStep`, `WriteArticleStep` are directly `new`'d only in three test files (`PlanQueriesStepTests.cs`, `GatherContextStepTests.cs`, `AggregateFactsStepTests.cs`, `ValidateFactsStepTests.cs`, `WriteArticleStepTests.cs`, `SourceEnrichmentIntegrationTests.cs`) and in `GenerateArticleJobTests.cs`. All of these construct the concrete class directly (`new PlanQueriesStep(...)`), which remains valid after the class additionally implements an interface — adding `: IPlanQueriesStep` to a class signature is purely additive and does not break any existing direct-instantiation test.
- There is no existing "one interface per pipeline step" precedent elsewhere in the codebase (`grep -rl "interface I.*Step"` returned nothing), so this establishes a new but unsurprising micro-pattern: a single-method interface named after its one implementation, matching the existing convention of `I{Entity}Service` / `I{Entity}Repository` pairs already used throughout the codebase (see `IOrderService` example in `development_guidelines.md`).

No architectural objection. This is low-risk, additive, and does not require any spec amendment on structural grounds.

## Proposed Architecture

### Component Overview

```
GenerateArticleHandler (MediatR handler)
        │  BackgroundJobClient.Enqueue<GenerateArticleJob>(...)
        ▼
GenerateArticleJob (Hangfire job, resolved by concrete type from DI)
        │  constructor now depends on 5 interfaces, not 5 concretions
        ▼
 IPlanQueriesStep ──▶ PlanQueriesStep
 IGatherContextStep ──▶ GatherContextStep
 IAggregateFactsStep ──▶ AggregateFactsStep
 IValidateFactsStep ──▶ ValidateFactsStep
 IWriteArticleStep ──▶ WriteArticleStep
        │  (each concrete step still depends on PipelineStepRecorder concretely — unchanged, out of scope)
        ▼
ArticlePipelineContext (shared mutable state, unchanged)
```

`GenerateArticleJob` remains the only consumer of the five new interfaces; each interface has exactly one production implementation. This is DIP applied at the narrowest possible seam — introducing an interface *only* where a test-time substitution boundary is needed, not speculatively across the whole pipeline (e.g. `PipelineStepRecorder` correctly stays concrete, per spec's Out of Scope section — it's an internal collaborator of each step, not of the job).

### Key Design Decisions

#### Decision 1: One interface per step vs. one shared `IPipelineStep` interface
**Options considered:**
- (a) Single shared `IPipelineStep { Task ExecuteAsync(ArticlePipelineContext, CancellationToken); }` interface implemented by all five classes.
- (b) Five distinct, per-step interfaces (`IPlanQueriesStep`, `IGatherContextStep`, etc.), each with an identical single-method signature, as specified.

**Chosen approach:** (b), per the spec.

**Rationale:** `GenerateArticleJob`'s constructor pins each step to an exact position and role in the fixed five-stage pipeline (order matters: plan → gather → aggregate → validate → write). A shared `IPipelineStep` would make the constructor `IPipelineStep, IPipelineStep, IPipelineStep, IPipelineStep, IPipelineStep`, which is ambiguous for DI resolution (five services registered against the same interface — `AddScoped<IPipelineStep, PlanQueriesStep>()` five times would collide, requiring keyed services or an ordered collection injection) and would let a future developer swap two steps' registration order by accident with the compiler unable to catch it. Distinct interfaces keep constructor-parameter-to-role mapping compiler-checked and keep the DI registration unambiguous with plain `AddScoped<TInterface, TImpl>()`. The minor duplication (five near-identical one-method interfaces) is the correct trade for explicitness here — this is not a case where a shared abstraction reduces real duplication, since each interface has exactly one implementation and one call site.

#### Decision 2: Interface file placement
**Options considered:**
- (a) Interfaces in their own files (e.g. `IPlanQueriesStep.cs`) alongside the class file.
- (b) Interface declared in the same file as its implementing class (as the spec/brief propose).

**Chosen approach:** (b) — interface and implementation share one file per step.

**Rationale:** The spec explicitly resolves this under "Open Questions" as an assumption, and it matches the brief's own code sample. This deviates from the more common C# convention of one type per file, but for a single-consumer, single-implementor interface that exists purely as a test seam, co-location keeps the change footprint minimal (5 files touched, not 10) and keeps the interface visually next to its only implementation, making it obvious this isn't a general-purpose abstraction. This is a defensible, narrow exception — do not generalize it to interfaces with multiple implementations or multiple consumers elsewhere in the codebase.

## Implementation Guidance

### Directory / Module Structure

No new files or directories. Modify in place:
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/PlanQueriesStep.cs` — add `IPlanQueriesStep` interface + `: IPlanQueriesStep` on the class.
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/GatherContextStep.cs` — same pattern for `IGatherContextStep`.
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/AggregateFactsStep.cs` — same pattern for `IAggregateFactsStep`.
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/ValidateFactsStep.cs` — same pattern for `IValidateFactsStep`.
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/WriteArticleStep.cs` — same pattern for `IWriteArticleStep`.
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/GenerateArticleJob.cs` — constructor/field types only (lines 13–17, 22–26).
- `backend/src/Anela.Heblo.Application/Features/Article/ArticleModule.cs` — lines 26–30, add interface to each `AddScoped<...>` call.
- `backend/test/Anela.Heblo.Tests/Article/Pipeline/GenerateArticleJobTests.cs` — rework `CreateJob(...)` to take `Mock<IXStep>`/`.Object` per FR-5.

### Interfaces and Contracts

Each interface: exactly one method, identical signature across all five:

```csharp
public interface IPlanQueriesStep
{
    Task ExecuteAsync(ArticlePipelineContext context, CancellationToken ct);
}
```

This is a private, module-internal contract — it does **not** belong in `Features/Article/Contracts/` (that folder is for cross-module or cross-use-case DTOs/interfaces per `development_guidelines.md`'s Contracts rules; these step interfaces have a single consumer inside one use case and are correctly scoped to `Pipeline/`, not promoted to `Contracts/`).

### Data Flow

Unchanged. `ArticlePipelineContext` continues to flow by reference through each `ExecuteAsync` call in the same fixed order (`PlanQueries → GatherContext → AggregateFacts → ValidateFacts → [status: Writing] → WriteArticle`); only the compile-time type of each dependency at the `GenerateArticleJob` constructor boundary changes, from concrete class to interface.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| A future developer adds a second implementation of one of these interfaces (e.g. a dev-mode no-op step) and registers it without updating the others, silently changing pipeline behavior for one stage only | Low | Out of scope per spec; NFR-3 already flags this. If it happens, it should be a deliberate, reviewed change — not blocked architecturally, just noted here so reviewers watch for it. |
| Test file (`GenerateArticleJobTests.cs`) still references now-unused `Mock<IChatClient>`, `Mock<IArticleKnowledgeSource>`, `Mock<IWebSearchClient>`, `Mock<IArticleStyleGuideSource>`, and `CreateNoOpRecorder()` after the refactor, producing dead test scaffolding | Low | Spec FR-5 already calls this out explicitly with acceptance criteria to remove unused mocks/usings; enforce during code review — `dotnet build` will not catch unused private fields, only unused `using` directives under nullable/analyzer settings if configured. |
| `WriteArticleStep` mock in the happy-path test must mutate `context` (`GeneratedTitle`, `GeneratedHtml`, `SourceRefs`) via `Callback<>`, which is easy to get subtly wrong (e.g. forgetting `SourceRefs` defaults to empty list vs. null, breaking the `foreach` in `RunAsync`) | Low | Spec already specifies this exact requirement (FR-5); implementer should initialize `SourceRefs` to a concrete list matching the existing JSON fixture's `sources_used`, mirroring today's real-step behavior being replaced. |
| Five near-identical interfaces could be mistaken for copy-paste error (wrong interface implemented by wrong class) | Low | Compiler enforces correctness at the `GenerateArticleJob` constructor call site — a mismatched interface/class pairing fails to compile since `ArticleModule.cs`'s `AddScoped<IPlanQueriesStep, GatherContextStep>()` (hypothetical mistake) would fail resolution only if `GatherContextStep` doesn't implement `IPlanQueriesStep`, which it won't. No runtime risk. |

## Specification Amendments

None required. The spec (FR-1 through FR-5, NFR-1 through NFR-3) is architecturally sound, internally consistent with existing module boundary and DI conventions, and its explicit "Out of Scope" section correctly excludes speculative extensions (alternate implementations, `PipelineStepRecorder` interface extraction, step-internals changes). The "Open Questions: None" / co-located-interface assumption is reasonable and matches the brief.

One clarifying note for the implementer, not a spec change: when removing unused mocks/usings from `GenerateArticleJobTests.cs` per FR-5, double-check `SourceEnrichmentIntegrationTests.cs` and the five per-step `*StepTests.cs` files independently — they construct the concrete step classes directly and are unaffected by this refactor, but they share the same `_chat`/`_knowledgeSource`/`_webSearch`/`_styleGuideSource` mock-field naming convention, so a search-and-replace across files risks touching the wrong file. Confirmed by inspection these are separate test classes in separate files, each with their own private mock fields — no shared base class — so this is a low-risk note, not a blocker.

## Prerequisites

None. No migrations, no config, no new infrastructure. This can be implemented directly against `main`/the current branch state. Standard validation gates apply before completion: `dotnet build`, `dotnet format`, and `dotnet test` for `Anela.Heblo.Tests` (per NFR-2 and this repo's global validation rules).
