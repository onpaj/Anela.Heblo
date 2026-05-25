# Specification: Remove Unused `IArticlePipelineStep` Interface

## Summary
The `IArticlePipelineStep` interface in the Article module is dead code — declared and implemented by five pipeline step classes, but never consumed via the abstraction (DI registration and injection both use concrete types). This spec removes the interface to eliminate false architectural signal, following YAGNI. If runtime step composition or polymorphic testability is needed in the future, the interface can be reintroduced at that time.

## Background
Architecture review on 2026-05-25 surfaced that `IArticlePipelineStep.cs` defines a single-method contract:

```csharp
public interface IArticlePipelineStep
{
    Task ExecuteAsync(ArticlePipelineContext context, CancellationToken ct);
}
```

Five classes implement it: `PlanQueriesStep`, `GatherContextStep`, `AggregateFactsStep`, `ValidateFactsStep`, `WriteArticleStep`.

However:
- `GenerateArticleJob.cs:14-18` injects the five concrete step types directly into its constructor — not the interface.
- `ArticleModule.cs:21-25` registers each step under its concrete type with the DI container — not against the interface.
- A repository-wide search reveals no consumer that resolves or injects `IArticlePipelineStep`.

The interface therefore exists as documentation only — it implies a polymorphism or testability benefit that the code does not realize. A reader encountering the interface reasonably expects to find `IEnumerable<IArticlePipelineStep>` injected somewhere, a step factory, or mocks of the interface in tests. None of these exist.

The pipeline is fixed and sequential. There is no requirement (current or in any backlog) for runtime-swappable steps. Option A from the brief (remove the interface) is the recommended path because it is honest about current usage, smaller in blast radius, and reversible.

## Functional Requirements

### FR-1: Delete the `IArticlePipelineStep` interface file
The file `IArticlePipelineStep.cs` (within the Article module's pipeline namespace) must be removed from the repository.

**Acceptance criteria:**
- `IArticlePipelineStep.cs` no longer exists in the working tree.
- No remaining file under the solution references the symbol `IArticlePipelineStep` (verified via repository-wide search).

### FR-2: Remove `IArticlePipelineStep` from step class declarations
Each of the five step classes — `PlanQueriesStep`, `GatherContextStep`, `AggregateFactsStep`, `ValidateFactsStep`, `WriteArticleStep` — currently declares `: IArticlePipelineStep`. The interface implementation marker must be removed from each class declaration. The `ExecuteAsync(ArticlePipelineContext, CancellationToken)` method on each class must remain unchanged — same signature, same body, same accessibility.

**Acceptance criteria:**
- None of the five step classes lists `IArticlePipelineStep` in its base type list.
- Each step class still exposes `public Task ExecuteAsync(ArticlePipelineContext context, CancellationToken ct)` with identical signature and behavior to before the change.
- No other public, protected, or internal members of these classes are added, removed, or renamed.

### FR-3: Preserve DI registrations and job composition
`ArticleModule.cs:21-25` already registers each step by its concrete type. `GenerateArticleJob.cs:14-18` already injects the five concrete step types. Neither registration nor injection should change. The pipeline must continue to execute the five steps in the same order with the same context.

**Acceptance criteria:**
- `ArticleModule.cs` registrations for the five step types are byte-equivalent to pre-change state (excluding any incidental whitespace).
- `GenerateArticleJob` constructor parameters and execution order are unchanged.
- Running `GenerateArticleJob` against an existing scenario produces the same observable behavior (same calls to downstream services, same `ArticlePipelineContext` mutations, same article output) as before.

### FR-4: Update or remove tests that reference the interface
If any unit or integration test asserts against, mocks, or names `IArticlePipelineStep`, it must be updated to reference the concrete step types (or removed if it only verifies the interface's existence). Test behavior must continue to validate the pipeline's functional outputs.

**Acceptance criteria:**
- No test file references `IArticlePipelineStep`.
- All Article-module tests pass (`dotnet test` for the relevant test project).
- Test coverage for the Article module does not regress.

### FR-5: Build, format, and validation clean
After the changes, the solution must build cleanly and pass formatting checks.

**Acceptance criteria:**
- `dotnet build` succeeds with zero new warnings related to the change.
- `dotnet format` produces no diff against the committed files.
- All previously passing tests still pass.

## Non-Functional Requirements

### NFR-1: Performance
No performance impact expected — the change is purely a type-system cleanup. Runtime behavior, allocation profile, and method dispatch are identical (the interface was never resolved at runtime).

### NFR-2: Security
No security surface affected. The interface carried no authorization, no input validation, and no cryptographic responsibilities. No secrets, no user input, no external boundaries are touched.

### NFR-3: Maintainability
Primary driver for this change. After completion, a reader of the Article pipeline code will see exactly what executes — concrete step classes injected into `GenerateArticleJob` — without an abstraction that implies unused flexibility.

### NFR-4: Reversibility
The change must be a single, atomic commit (or a small, easily-reverted set of commits) so it can be rolled back trivially if a future requirement reintroduces the need for the interface.

## Data Model
No data model changes. No entities, database schemas, or persisted contracts are affected.

## API / Interface Design
- **Public HTTP/MediatR contracts:** unchanged.
- **OpenAPI client:** unchanged (the interface is internal to the Article module).
- **DI container surface:** unchanged externally; each step continues to be resolvable by its concrete type.
- **Internal C# surface:** `IArticlePipelineStep` is removed. The five step classes lose one interface from their declaration but retain their `ExecuteAsync` method.

No UI flows, no public API endpoints, no events, and no cross-module contracts are affected.

## Dependencies
- Article module source (`backend/.../Article/...`) — exact paths per `docs/architecture/filesystem.md`.
- Article module DI registration (`ArticleModule.cs`).
- Article module job composition (`GenerateArticleJob.cs`).
- Article module test project (any tests that reference the interface).

No external libraries, no NuGet packages, no third-party services are involved.

## Out of Scope
- **Introducing `IEnumerable<IArticlePipelineStep>` injection (Option B from the brief).** Explicitly not pursued — there is no current need for runtime step composition.
- **Refactoring `GenerateArticleJob` to use a step collection or pipeline builder pattern.** Out of scope; the fixed sequential composition is acceptable.
- **Adding new tests beyond what is needed to keep coverage stable.** This is a deletion, not a feature.
- **Renaming, restructuring, or moving the step classes themselves.** Only the interface implementation marker is removed; class names, namespaces, and file paths stay put.
- **Documentation updates beyond removing stale references.** If the interface is mentioned in `docs/` it should be removed; no new architecture docs are written.
- **Performance tuning of the pipeline.** Untouched.

## Open Questions
None.

## Status: COMPLETE