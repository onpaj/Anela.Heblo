# Specification: Decouple GatherContextStep from KnowledgeBase via Article-Owned Contract

## Summary
The `GatherContextStep` in the Article module currently takes a compile-time dependency on `SearchDocumentsRequest`, an internal UseCase type owned by the KnowledgeBase module, violating the project's module-boundary discipline. This work introduces a consumer-owned contract (`IArticleKnowledgeSource`) in the Article module and a KnowledgeBase-side adapter that fulfills it, removing the direct cross-module coupling while preserving current runtime behavior.

## Background
The repository follows Clean Architecture with Vertical Slice organization, and `docs/architecture/development_guidelines.md` is explicit: "Communication between modules exclusively through `contracts/`." Two patterns currently coexist in the Article module:

- **Correct:** Access to the article style guide via `IArticleStyleGuideSource` (defined in `Article/Contracts/`, implemented by an adapter in KnowledgeBase).
- **Incorrect:** Access to KB document search via direct `IMediator.Send(new SearchDocumentsRequest { ... })` from `GatherContextStep`, importing a UseCase-internal type from KnowledgeBase.

The same shortcut exists in `Smartsupp/UseCases/GenerateDraftReply/GenerateDraftReplyHandler.cs`, so this is a recurring pattern that needs a canonical fix. The existing `ModuleBoundariesTests` namespace allowlist allows `Article → KnowledgeBase` references, so this violation is not currently caught.

This refactor aligns the KB-search path with the already-established style-guide contract pattern within the same module, eliminating the inconsistency and removing a fragile compile-time coupling.

## Functional Requirements

### FR-1: Define `IArticleKnowledgeSource` contract in Article module
Introduce a new contract interface and its DTO in the Article module's `Contracts/` folder. The contract exposes only what Article needs to consume — a search method returning a typed list of knowledge chunks.

**File:** `backend/src/Anela.Heblo.Application/Features/Article/Contracts/IArticleKnowledgeSource.cs`

**Shape:**
```csharp
public interface IArticleKnowledgeSource
{
    Task<IReadOnlyList<ArticleKnowledgeChunk>> SearchAsync(
        string query, int topK, CancellationToken ct);
}

public class ArticleKnowledgeChunk
{
    public Guid ChunkId { get; set; }
    public string SourceFilename { get; set; } = "";
    public string Content { get; set; } = "";
    public double Score { get; set; }
}
```

**Acceptance criteria:**
- File exists at the path above under the namespace `Anela.Heblo.Application.Features.Article.Contracts`.
- `ArticleKnowledgeChunk` is a class (not a record), consistent with project DTO rules.
- The contract has no `using` reference to any KnowledgeBase namespace.
- All four chunk fields (`ChunkId`, `SourceFilename`, `Content`, `Score`) map 1:1 to fields currently consumed by `GatherContextStep` from the KB search response.

### FR-2: Implement `KnowledgeBaseArticleSourceAdapter` in KnowledgeBase module
Add an adapter inside the KnowledgeBase module that implements `IArticleKnowledgeSource` by dispatching `SearchDocumentsRequest` through MediatR and mapping the response into the Article-owned DTO.

**File:** `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/KnowledgeBaseArticleSourceAdapter.cs` (create `Infrastructure/` folder if it does not exist — verify against `filesystem.md` conventions; if a different folder is used by the existing `IArticleStyleGuideSource` adapter, mirror that location for consistency).

**Acceptance criteria:**
- The adapter class is `sealed`, internal-visible if the existing style-guide adapter is, otherwise `public sealed`.
- The adapter takes `IMediator` via constructor injection.
- `SearchAsync` issues `_mediator.Send(new SearchDocumentsRequest { Query = query, TopK = topK }, ct)` and projects the response chunks 1:1 into `ArticleKnowledgeChunk`.
- The returned collection is materialized (e.g., `.ToArray()`) so callers do not observe deferred enumeration.
- Behavior under cancellation and exceptions is unchanged relative to the current direct call.

### FR-3: Register the adapter binding in KnowledgeBase module composition
Register `IArticleKnowledgeSource → KnowledgeBaseArticleSourceAdapter` with the DI container in the KnowledgeBase module's registration entry point.

**Acceptance criteria:**
- Registration is added to `KnowledgeBaseModule.cs` (or the equivalent KB module-registration class — match the convention used for the existing `IArticleStyleGuideSource` binding).
- Lifetime is `Scoped`, matching the existing style-guide adapter binding.
- Registration is performed by the KnowledgeBase module (not by the Article module), preserving the rule that providers own their bindings.

### FR-4: Refactor `GatherContextStep` to depend on `IArticleKnowledgeSource`
Replace the `IMediator`-based KB search path in `GatherContextStep` with the new contract.

**File:** `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/GatherContextStep.cs`

**Acceptance criteria:**
- The `using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;` import is removed.
- The class no longer references `SearchDocumentsRequest` or the KnowledgeBase namespace anywhere.
- A new constructor parameter `IArticleKnowledgeSource knowledgeSource` is added; it is stored as a private readonly field.
- If `IMediator` was only used for the KB search and is not used elsewhere in the class, the `IMediator` dependency is removed. If `IMediator` is still needed for other paths, it remains.
- The KB-gathering helper (`GatherKnowledgeBaseSnippetsAsync` or equivalent) calls `_knowledgeSource.SearchAsync(query, _options.KnowledgeBaseTopK, ct)` and produces the same `ContextSnippet` collection as before.
- Field-level mapping from `ArticleKnowledgeChunk` to `ContextSnippet` preserves current semantics (snippet content, source attribution, score).

### FR-5: Update tests for `GatherContextStep`
Any existing unit tests for `GatherContextStep` that mock `IMediator` for the KB search path must be updated to mock `IArticleKnowledgeSource` instead.

**Acceptance criteria:**
- All existing tests for `GatherContextStep` pass after the refactor.
- Tests that previously verified `IMediator.Send` was called with `SearchDocumentsRequest` now verify `IArticleKnowledgeSource.SearchAsync` was called with the expected `query` and `topK`.
- Test mocks return `ArticleKnowledgeChunk` instances; assertions on the resulting `ContextSnippet` shape are unchanged.
- No new test classes are required by this refactor (existing coverage scope is preserved).

### FR-6: Update `ModuleBoundariesTests` if needed
Verify that with the refactor in place, `GatherContextStep` no longer references the `KnowledgeBase` namespace. If the existing namespace allowlist for `Article → KnowledgeBase` is now unused by Article (because all remaining references go through the adapter inside KnowledgeBase), tighten the rule accordingly.

**Acceptance criteria:**
- `ModuleBoundariesTests` continues to pass.
- If the only legitimate Article→KnowledgeBase reference was the one being removed, the allowlist entry is removed; otherwise it is left untouched.
- A comment in the test or a new dedicated assertion documents the intent (optional — only if the existing test style includes such commentary).

### FR-7: Smartsupp is out of scope for this work item
`Smartsupp/UseCases/GenerateDraftReply/GenerateDraftReplyHandler.cs` exhibits the same coupling but is explicitly deferred to a follow-up. This spec covers Article only.

**Acceptance criteria:**
- No changes are made to Smartsupp source files in this work item.
- A follow-up issue or task is recorded (see Open Questions / Out of Scope) to apply the same pattern to Smartsupp.

## Non-Functional Requirements

### NFR-1: Performance
No measurable performance change. The adapter adds a single method call and a projection over a small (`topK`) collection. The MediatR dispatch path is unchanged. No additional allocations beyond the new chunk DTO instances, which are short-lived.

### NFR-2: Security
No change to authentication, authorization, or data exposure. The adapter exposes exactly the same data the caller already accessed; only the type boundary changes.

### NFR-3: Backwards compatibility
Pure internal refactor. No public API, HTTP endpoint, OpenAPI schema, or persisted data shape changes. No database migration required. No generated TypeScript client changes.

### NFR-4: Build and validation
- `dotnet build` succeeds.
- `dotnet format` produces no diff.
- All existing tests pass, including `ModuleBoundariesTests` and any `GatherContextStep` unit tests.
- No new analyzer warnings introduced.

## Data Model
No persistent data model changes. One new transport DTO is introduced:

- **`ArticleKnowledgeChunk`** (Article module, `Contracts/`): plain DTO with `ChunkId : Guid`, `SourceFilename : string`, `Content : string`, `Score : double`. Mirrors the subset of fields `GatherContextStep` currently consumes from `SearchDocumentsResponse.Chunks`.

## API / Interface Design

### New contract (Article module)
```csharp
namespace Anela.Heblo.Application.Features.Article.Contracts;

public interface IArticleKnowledgeSource
{
    Task<IReadOnlyList<ArticleKnowledgeChunk>> SearchAsync(
        string query, int topK, CancellationToken ct);
}

public class ArticleKnowledgeChunk
{
    public Guid ChunkId { get; set; }
    public string SourceFilename { get; set; } = "";
    public string Content { get; set; } = "";
    public double Score { get; set; }
}
```

### New adapter (KnowledgeBase module)
`KnowledgeBaseArticleSourceAdapter : IArticleKnowledgeSource`, scoped lifetime, constructor-injected `IMediator`, internally dispatches `SearchDocumentsRequest` and projects the response.

### DI registration
Added to KnowledgeBase module's composition root (`KnowledgeBaseModule.cs` or equivalent):
```csharp
services.AddScoped<IArticleKnowledgeSource, KnowledgeBaseArticleSourceAdapter>();
```

### Refactored consumer
`GatherContextStep` constructor parameter list gains `IArticleKnowledgeSource knowledgeSource`. The KB-search branch calls `_knowledgeSource.SearchAsync(...)` instead of `_mediator.Send(new SearchDocumentsRequest { ... })`. The `using` import for `KnowledgeBase.UseCases.SearchDocuments` is removed.

## Dependencies
- **MediatR** — still used inside the new adapter; no version change.
- **Existing `SearchDocumentsRequest` / `SearchDocumentsResponse` / `SearchDocumentsHandler`** — unchanged. They remain the canonical KB search implementation; the adapter is a thin wrapper.
- **Existing `IArticleStyleGuideSource` pattern** — used as the reference implementation for folder placement, registration style, and adapter conventions.
- **`ModuleBoundariesTests`** — must continue to pass; potentially tightened.

## Out of Scope
- **Smartsupp module refactor.** `GenerateDraftReplyHandler` will be updated in a follow-up work item using the same pattern (likely a separate `ISmartsuppKnowledgeSource` contract owned by Smartsupp, since contracts are consumer-owned per the existing convention).
- **Changes to `SearchDocumentsRequest` / `SearchDocumentsResponse` shape.** No fields are added or removed. No renames.
- **Changes to the KnowledgeBase search algorithm, ranking, or storage.** Pure pass-through.
- **New unit tests for the adapter itself.** The adapter is a trivial projection; existing integration coverage of the KB search path remains authoritative. (Can be revisited if reviewers disagree.)
- **Public API or frontend changes.** Nothing exposed beyond module boundaries changes shape.
- **Tightening of `ModuleBoundariesTests` rules beyond what this refactor enables.** Broader boundary-rule cleanup is a separate concern.

## Open Questions
None.

## Status: COMPLETE