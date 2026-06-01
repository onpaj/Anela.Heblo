# Specification: Decouple Article Module from KnowledgeBase Internals via Consumer-Owned Contract

## Summary
The `GatherContextStep` in the Article module currently depends on `SearchDocumentsRequest`, an internal MediatR request type owned by the KnowledgeBase module's `UseCases/` folder. This violates the module boundary rule that cross-module communication must go through `Contracts/`. We will introduce a consumer-owned `IArticleKnowledgeSource` contract in the Article module, implemented by a KnowledgeBase adapter, mirroring the existing `IArticleStyleGuideSource` pattern.

## Background
`development_guidelines.md` mandates: *"Communication between modules exclusively through `contracts/`."* The Article module already follows this pattern correctly for style-guide access — `IArticleStyleGuideSource` lives in `Article/Contracts/` and is implemented by a KnowledgeBase adapter. However, the knowledge-base search path in `GatherContextStep` bypasses the contract layer entirely and sends a KnowledgeBase-internal MediatR request directly:

```csharp
// backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/GatherContextStep.cs:2
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;
...
var response = await _mediator.Send(
    new SearchDocumentsRequest { Query = query, TopK = _options.KnowledgeBaseTopK }, ct);
```

This creates hard compile-time coupling: any rename, restructuring, or split of `SearchDocumentsRequest`/`SearchDocumentsResponse` inside KnowledgeBase will break Article (and Smartsupp, which uses the same shortcut). The existing `ModuleBoundariesTests` namespace allowlist permits the coupling today but does not enforce contract-only access, so the violation slipped in and has already set a precedent across two modules. This spec addresses the Article-side cleanup; the Smartsupp equivalent is explicitly deferred as a follow-up.

## Functional Requirements

### FR-1: Define `IArticleKnowledgeSource` contract in Article module
Introduce a new contract owned by the Article module that exposes knowledge-base search capability in Article-domain terms.

**Location:** `backend/src/Anela.Heblo.Application/Features/Article/Contracts/IArticleKnowledgeSource.cs`

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
- File exists at the specified path, under the `Anela.Heblo.Application.Features.Article.Contracts` namespace.
- `ArticleKnowledgeChunk` is a class (not a record), consistent with project DTO conventions.
- The interface and DTO contain no references to KnowledgeBase types (no `SearchDocumentsRequest`, `SearchDocumentsResponse`, or chunk types from that module).
- The contract sits alongside `IArticleStyleGuideSource` in the same `Contracts/` folder.

### FR-2: KnowledgeBase provides adapter implementation
Implement `IArticleKnowledgeSource` inside the KnowledgeBase module by delegating to the existing `SearchDocumentsRequest` MediatR handler.

**Location:** `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/KnowledgeBaseArticleSourceAdapter.cs`

**Shape:**
```csharp
public sealed class KnowledgeBaseArticleSourceAdapter : IArticleKnowledgeSource
{
    private readonly IMediator _mediator;
    public KnowledgeBaseArticleSourceAdapter(IMediator mediator) => _mediator = mediator;

    public async Task<IReadOnlyList<ArticleKnowledgeChunk>> SearchAsync(
        string query, int topK, CancellationToken ct)
    {
        var response = await _mediator.Send(
            new SearchDocumentsRequest { Query = query, TopK = topK }, ct);
        return response.Chunks.Select(c => new ArticleKnowledgeChunk
        {
            ChunkId = c.ChunkId,
            SourceFilename = c.SourceFilename,
            Content = c.Content,
            Score = c.Score,
        }).ToArray();
    }
}
```

**Acceptance criteria:**
- Adapter lives in the KnowledgeBase module's `Infrastructure/` folder (mirroring how `IArticleStyleGuideSource`'s KB-side adapter is organized).
- Adapter is `sealed` and constructor-injects `IMediator`.
- Adapter projects every field present on `SearchDocumentsResponse.Chunks` items into `ArticleKnowledgeChunk` (chunk id, source filename, content, score). Field-by-field projection is explicit; no AutoMapper.
- Adapter returns `IReadOnlyList<ArticleKnowledgeChunk>` and never `null` (returns an empty list when the underlying response has none).
- Cancellation token is propagated to `_mediator.Send`.

### FR-3: Register the adapter in `KnowledgeBaseModule`
The KnowledgeBase module registers the binding so that any consumer of `IArticleKnowledgeSource` resolves to the adapter.

**Location:** `KnowledgeBaseModule.cs` (the module's DI registration class).

**Acceptance criteria:**
- `services.AddScoped<IArticleKnowledgeSource, KnowledgeBaseArticleSourceAdapter>();` is added alongside the existing `IArticleStyleGuideSource` registration.
- Scope is `Scoped`, matching the existing pattern for analogous adapters.
- No registration exists in the Article module for this interface (Article owns the contract; KnowledgeBase owns the binding).

### FR-4: `GatherContextStep` consumes `IArticleKnowledgeSource`
Replace the direct MediatR + `SearchDocumentsRequest` call in `GatherContextStep` with the new contract.

**Location:** `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/GatherContextStep.cs`

**Acceptance criteria:**
- The `using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;` import is removed.
- A constructor-injected `IArticleKnowledgeSource _knowledgeSource` field is added.
- `GatherKnowledgeBaseSnippetsAsync` (or the equivalent KB-fetch branch) calls `_knowledgeSource.SearchAsync(query, _options.KnowledgeBaseTopK, ct)` and maps `ArticleKnowledgeChunk` items into `ContextSnippet` instances using the same field mapping currently applied to `SearchDocumentsResponse.Chunks`.
- The `IMediator` dependency is retained only if it is still used by other branches of `GatherContextStep`; if the KB path was its sole consumer, `IMediator` is removed from the constructor.
- Behavior is unchanged: same query, same `TopK`, same resulting `ContextSnippet` shape, same ordering, same cancellation semantics.

### FR-5: Update / extend tests for `GatherContextStep`
Existing unit tests for `GatherContextStep` must continue to pass with the new dependency, and a new test must verify the contract-mediated path.

**Acceptance criteria:**
- All existing `GatherContextStep` unit tests pass after switching the mock from `IMediator` (KB branch) to `IArticleKnowledgeSource`.
- At least one unit test exercises the KB branch by stubbing `IArticleKnowledgeSource.SearchAsync` and asserting that returned chunks become `ContextSnippet` entries with the expected field values.
- A unit test (new or existing) for `KnowledgeBaseArticleSourceAdapter` asserts that it dispatches `SearchDocumentsRequest` with the given `query`/`topK` and maps `SearchDocumentsResponse.Chunks` field-for-field into `ArticleKnowledgeChunk`.

### FR-6: Architecture test enforces contract-only cross-module access for this path
Strengthen `ModuleBoundariesTests` so a future regression (e.g., re-introducing `using ...KnowledgeBase.UseCases.SearchDocuments`) fails at build/test time.

**Acceptance criteria:**
- A test asserts that no type under `Anela.Heblo.Application.Features.Article` references any type whose namespace matches `Anela.Heblo.Application.Features.KnowledgeBase.UseCases.*`.
- The test exempts (or is scoped to exclude) the KnowledgeBase-side adapter itself, since the adapter legitimately bridges the two.
- The test fails with a clear message naming the offending caller and the forbidden namespace.

### FR-7: No behavior change for end users or downstream pipeline steps
The Article generation pipeline must produce the same `ContextSnippet` set for any given input it did before the refactor.

**Acceptance criteria:**
- Snapshot/golden tests covering the article-generation pipeline (if any exist) continue to pass without snapshot updates.
- Manual smoke check: invoking the Article generation pipeline against a fixture article that triggers a KB lookup yields the same number of snippets, same source filenames, and same content text as before.

## Non-Functional Requirements

### NFR-1: Performance
- The adapter adds one projection (`Select(...).ToArray()`) over the response chunks. Acceptable overhead given typical `TopK ≤ 20`.
- No additional network or database calls are introduced.
- No regression in `GatherContextStep` end-to-end latency beyond the projection cost above.

### NFR-2: Architecture compliance
- After this change, the Article module contains zero `using` statements referencing `Anela.Heblo.Application.Features.KnowledgeBase.UseCases.*`.
- The new contract follows the same conventions as `IArticleStyleGuideSource` (location, naming, scope, sealed adapter).

### NFR-3: Backward compatibility
- `SearchDocumentsRequest`, `SearchDocumentsResponse`, and the existing `SearchDocuments` MediatR handler remain unchanged. Other consumers (e.g., direct API callers, Smartsupp) are not touched by this spec.

### NFR-4: Validation
- `dotnet build` and `dotnet format` succeed.
- All BE unit + integration tests pass.
- `ModuleBoundariesTests` pass, including the new assertion in FR-6.

## Data Model

**New DTO:** `ArticleKnowledgeChunk` (class, not record — per project DTO rules)

| Field            | Type     | Source                                                  |
|------------------|----------|---------------------------------------------------------|
| `ChunkId`        | `Guid`   | `SearchDocumentsResponse.Chunks[i].ChunkId`             |
| `SourceFilename` | `string` | `SearchDocumentsResponse.Chunks[i].SourceFilename`      |
| `Content`        | `string` | `SearchDocumentsResponse.Chunks[i].Content`             |
| `Score`          | `double` | `SearchDocumentsResponse.Chunks[i].Score`               |

`SearchDocumentsRequest` / `SearchDocumentsResponse` and their existing handler are unchanged.

## API / Interface Design

**New contract:**
```csharp
namespace Anela.Heblo.Application.Features.Article.Contracts;

public interface IArticleKnowledgeSource
{
    Task<IReadOnlyList<ArticleKnowledgeChunk>> SearchAsync(
        string query, int topK, CancellationToken ct);
}
```

**Adapter (KnowledgeBase side):**
- `KnowledgeBaseArticleSourceAdapter : IArticleKnowledgeSource`, dispatches `SearchDocumentsRequest` via `IMediator`.

**Caller change (Article side):**
- `GatherContextStep` constructor swaps direct `IMediator`-for-KB usage for `IArticleKnowledgeSource _knowledgeSource`.

No HTTP endpoints, UI flows, or external API surfaces are added or modified.

## Dependencies
- **Existing `SearchDocuments` use case** in the KnowledgeBase module (request, response, handler) — used unchanged by the adapter.
- **MediatR** — used by the adapter only; no longer used by `GatherContextStep` for the KB path.
- **Existing DI infrastructure** in `KnowledgeBaseModule` (mirrors `IArticleStyleGuideSource` registration).
- **Existing `IArticleStyleGuideSource` pattern** in `Article/Contracts/` — used as the reference template.

## Out of Scope
- **Smartsupp/`GenerateDraftReplyHandler` cleanup.** The same coupling exists at `Smartsupp/UseCases/GenerateDraftReply/GenerateDraftReplyHandler.cs:1` and should be fixed with the same contract-owned-by-consumer pattern, but it is explicitly deferred to a follow-up task.
- **Restructuring or renaming `SearchDocumentsRequest` / `SearchDocumentsResponse`.** They remain as-is; only the cross-module coupling is removed.
- **Replacing MediatR with a different dispatch mechanism** inside the adapter.
- **Adding caching, retries, or telemetry** to the new code path.
- **Frontend changes.** This refactor is BE-only.
- **Database / migration changes.** None required.

## Open Questions
None.

## Status: COMPLETE