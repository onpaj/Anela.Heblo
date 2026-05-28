I have full context. Now I'll produce the architecture review.

# Architecture Review: Decouple GatherContextStep from KnowledgeBase via Article-Owned Contract

## Skip Design: true

## Architectural Fit Assessment

The proposed change is a textbook application of an already-established pattern in this codebase. The codebase explicitly documents the **consumer-owned contract / provider-implemented adapter** pattern in `docs/architecture/development_guidelines.md` (lines 196-205) and has two living examples in the exact same `Application` assembly:

1. `Leaflet.Contracts.ILeafletKnowledgeSource` ← `KnowledgeBase.Infrastructure.KnowledgeBaseLeafletSourceAdapter`
2. `Article.Contracts.IArticleStyleGuideSource` ← `KnowledgeBase.Infrastructure.KnowledgeBaseArticleStyleGuideSource`

Both adapters are `internal sealed`, constructor-injected, scoped, and registered inside `KnowledgeBaseModule.AddKnowledgeBaseModule(...)`. The new `IArticleKnowledgeSource` slots in beside them with zero structural friction. The `ModuleBoundariesTests` allowlist already contains three entries (`SearchDocumentsRequest`, `SearchDocumentsResponse`, `ChunkResult`) tagged as removable when this exact refactor lands — the boundary test will get tighter as a direct byproduct.

Integration points:
- **Consumer**: `Article.UseCases.Generate.Pipeline.GatherContextStep` (the only Article→KB compile-time coupling left).
- **Provider**: KB's `SearchDocuments` MediatR pipeline (untouched).
- **Composition**: `KnowledgeBaseModule.cs`.
- **Architecture tests**: `ModuleBoundariesTests.ArticleAllowlist`.

No conflict with existing patterns. No new architectural concept is introduced.

## Proposed Architecture

### Component Overview

```
┌────────────────────────────────────────────────────────────────────────┐
│ Anela.Heblo.Application                                                │
│                                                                        │
│   Features/Article/                                                    │
│   ├── Contracts/                                                       │
│   │   ├── IArticleStyleGuideSource.cs       (existing)                 │
│   │   ├── IArticleUserResolver.cs           (existing)                 │
│   │   └── IArticleKnowledgeSource.cs        ← NEW                      │
│   │       • interface IArticleKnowledgeSource                          │
│   │       • class ArticleKnowledgeChunk (DTO, not record)              │
│   │                                                                    │
│   └── UseCases/Generate/Pipeline/                                      │
│       └── GatherContextStep.cs              ← MODIFIED                 │
│           • depends on IArticleKnowledgeSource                         │
│           • IMediator removed (only KB search used it)                 │
│                                                                        │
│                              ▲ implements                              │
│                              │                                         │
│   Features/KnowledgeBase/                                              │
│   ├── Infrastructure/                                                  │
│   │   ├── KnowledgeBaseArticleStyleGuideSource.cs   (existing)         │
│   │   ├── KnowledgeBaseLeafletSourceAdapter.cs      (existing)         │
│   │   └── KnowledgeBaseArticleSourceAdapter.cs      ← NEW              │
│   │       • internal sealed                                            │
│   │       • injects IMediator                                          │
│   │       • dispatches SearchDocumentsRequest                          │
│   │       • projects ChunkResult → ArticleKnowledgeChunk               │
│   │                                                                    │
│   ├── UseCases/SearchDocuments/             (unchanged)                │
│   └── KnowledgeBaseModule.cs                ← MODIFIED                 │
│       • + AddScoped<IArticleKnowledgeSource,                           │
│                     KnowledgeBaseArticleSourceAdapter>()               │
└────────────────────────────────────────────────────────────────────────┘

Test assembly
   └── Architecture/ModuleBoundariesTests.cs   ← MODIFIED
       • remove 3 ArticleAllowlist entries (SearchDocumentsRequest/
         Response/ChunkResult). Allowlist becomes empty but is retained
         as a non-null HashSet so the rule entry still compiles.
   └── Article/Pipeline/GatherContextStepTests.cs ← MODIFIED
       • mock IArticleKnowledgeSource instead of IMediator for KB path
       • SearchDocumentsRequest/Response/ChunkResult imports removed
```

### Key Design Decisions

#### Decision 1: Adapter visibility — `internal sealed`
**Options considered:**
- A. `public sealed` (mirrors public surface of UseCase handlers).
- B. `internal sealed` (mirrors the two existing adapters in `KnowledgeBase.Infrastructure`).

**Chosen approach:** `internal sealed`.

**Rationale:** Both `KnowledgeBaseArticleStyleGuideSource` and `KnowledgeBaseLeafletSourceAdapter` are `internal sealed`. The adapter is a composition-root binding target only — no caller outside the assembly should reference the concrete type. The spec's "internal-visible if the existing style-guide adapter is" clause resolves unambiguously to `internal`.

#### Decision 2: Folder placement — `KnowledgeBase/Infrastructure/`
**Options considered:**
- A. New `KnowledgeBase/Adapters/` folder.
- B. Place beside the two existing adapters in `KnowledgeBase/Infrastructure/`.

**Chosen approach:** B. Directly in `Features/KnowledgeBase/Infrastructure/`.

**Rationale:** The folder already exists (`Infrastructure/` contains both peer adapters plus a `Jobs/` subfolder). The spec's "create `Infrastructure/` folder if it does not exist" guard is therefore moot — verified. `filesystem.md` lines 33 and 117 codify `Features/{Feature}/Infrastructure/` for feature-specific infrastructure, and the two reference adapters confirm it for cross-module adapters specifically.

#### Decision 3: Drop `IMediator` from `GatherContextStep`
**Options considered:**
- A. Keep `IMediator` injected "for future use".
- B. Remove `IMediator` since it's only used by the KB search path.

**Chosen approach:** B. Remove `IMediator` from the constructor.

**Rationale:** Per the read of `GatherContextStep.cs`, `_mediator` is referenced exactly once — line 88, the KB search. The other three dependencies (`_webSearch`, `_styleGuideSource`, `_recorder`) cover web search, style guide load, and step recording. The CLAUDE.md rule "Don't add features … beyond what the task requires" plus "Surgical changes" backs removing the now-unused dependency. The test class will need its `Mock<IMediator>` removed correspondingly. (The spec's FR-4 already authorizes this.)

#### Decision 4: Adapter projection materialization
**Options considered:**
- A. Return `IEnumerable<ArticleKnowledgeChunk>` (deferred).
- B. Materialize to `.ToArray()` and return `IReadOnlyList<ArticleKnowledgeChunk>`.

**Chosen approach:** B. Materialize with `.ToArray()`.

**Rationale:** Contract signature is `Task<IReadOnlyList<ArticleKnowledgeChunk>>`. The KB call has already executed and produced a fully materialized `List<ChunkResult>` before projection — there is nothing to "defer". `.ToArray()` is cheaper than `.ToList()` for a fixed-size projection (`topK ≤ 20` per `SearchDocumentsRequest` validation) and aligns with the spec's explicit instruction in FR-2.

#### Decision 5: Exception/cancellation passthrough
**Options considered:**
- A. Adapter catches and translates exceptions to a contract-specific type.
- B. Adapter is a transparent passthrough; the consumer keeps its existing `try/catch (Exception ex) when (ex is not OperationCanceledException)`.

**Chosen approach:** B. Passthrough.

**Rationale:** The current `GatherContextStep.GatherKnowledgeBaseSnippetsAsync` wraps the call in `try/catch` and logs `KB search failed for query '{Query}'`. Preserving this catch site (now wrapping the contract call instead of the MediatR call) keeps observability identical. Introducing a contract-defined exception type would expand scope and have no caller. NFR-1 and the spec's "Behavior under cancellation and exceptions is unchanged" requirement are met by doing nothing in the adapter.

## Implementation Guidance

### Directory / Module Structure

Three files change, two files are created. No new folders.

**Create:**
- `backend/src/Anela.Heblo.Application/Features/Article/Contracts/IArticleKnowledgeSource.cs`
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/KnowledgeBaseArticleSourceAdapter.cs`

**Modify:**
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/GatherContextStep.cs` — remove `using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;`, remove `IMediator _mediator` field + ctor parameter, add `IArticleKnowledgeSource _knowledgeSource`, rewrite `GatherKnowledgeBaseSnippetsAsync` to call `_knowledgeSource.SearchAsync(query, _options.KnowledgeBaseTopK, ct)` and project `ArticleKnowledgeChunk → ContextSnippet`.
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs` — append one `services.AddScoped<IArticleKnowledgeSource, KnowledgeBaseArticleSourceAdapter>();` next to the existing `IArticleStyleGuideSource` binding (line 43), with a one-line comment matching the style of lines 41-42.
- `backend/test/Anela.Heblo.Tests/Article/Pipeline/GatherContextStepTests.cs` — replace `Mock<IMediator>` with `Mock<IArticleKnowledgeSource>`, replace `SearchDocumentsRequest/Response/ChunkResult` setup data with `ArticleKnowledgeChunk` instances, update the `Send` verify in `ExecuteAsync_KnowledgeBaseEnabled_AddsKbSnippets` to verify `SearchAsync(query, _options.KnowledgeBaseTopK, …)`, remove `using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;`. Keep all 5 test scenarios; assertion shape on `ContextSnippet` is unchanged.
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — delete the three entries in `ArticleAllowlist` (lines 58-60) and update the explanatory comment (lines 52-57) to read as a historical note that the violation has been removed, or remove the comment entirely. **Keep the `ArticleAllowlist` field declaration** (as an empty `HashSet`) so the `Rules()` `TheoryData` row at line 107 still compiles. Mirror the style of the existing `PurchaseAllowlist` (line 83) — an empty allowlist with a single comment line.

### Interfaces and Contracts

```csharp
// File: Features/Article/Contracts/IArticleKnowledgeSource.cs
namespace Anela.Heblo.Application.Features.Article.Contracts;

/// <summary>
/// Retrieves knowledge-base snippets for article generation context.
/// Implemented by the KnowledgeBase module via an adapter.
/// </summary>
public interface IArticleKnowledgeSource
{
    Task<IReadOnlyList<ArticleKnowledgeChunk>> SearchAsync(
        string query, int topK, CancellationToken cancellationToken);
}

public class ArticleKnowledgeChunk   // class, not record (project DTO rule)
{
    public Guid ChunkId { get; set; }
    public string SourceFilename { get; set; } = "";
    public string Content { get; set; } = "";
    public double Score { get; set; }
}
```

Mandatory invariants:
- The file has **no** `using Anela.Heblo.Application.Features.KnowledgeBase…` statement.
- `ArticleKnowledgeChunk` is a `class`, matching the project rule about C# records and OpenAPI generators (CLAUDE.md). While this DTO is not on the public API surface, the convention is uniform across the Article `Contracts/` folder.
- Cancellation token parameter is named `cancellationToken` (full word), to match the existing `IArticleStyleGuideSource` style.

### Data Flow

```
ArticlePipelineContext.SearchQueries (List<string>)
        │
        ▼ for each query
GatherContextStep.GatherKnowledgeBaseSnippetsAsync
        │
        │ _knowledgeSource.SearchAsync(query, _options.KnowledgeBaseTopK, ct)
        ▼
IArticleKnowledgeSource (Article.Contracts)
        │
        ▼  resolved by DI to →
KnowledgeBaseArticleSourceAdapter (KnowledgeBase.Infrastructure)
        │
        │ _mediator.Send(new SearchDocumentsRequest { Query, TopK }, ct)
        ▼
SearchDocumentsHandler  → SearchDocumentsResponse { Chunks: List<ChunkResult> }
        │
        ▼ projection in adapter
IReadOnlyList<ArticleKnowledgeChunk>
   { ChunkId, SourceFilename, Content, Score }
        │
        ▼ projection in GatherContextStep
List<ContextSnippet>
   { Source = KnowledgeBase, Title = SourceFilename, Excerpt = Content,
     Url = null, ChunkId, Score }
        │
        ▼
ArticlePipelineContext.ContextSnippets
```

Exception path (unchanged in behaviour):
- `OperationCanceledException` propagates through both the adapter and `GatherContextStep` (the `catch (Exception ex) when (ex is not OperationCanceledException)` filter is preserved verbatim in the consumer).
- Any other exception inside the adapter or downstream handler is caught in `GatherKnowledgeBaseSnippetsAsync` and logged with `_logger.LogWarning(ex, "KB search failed for query '{Query}'", query)` — the per-query partial-failure semantic is unchanged.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `ModuleBoundariesTests.ArticleAllowlist` left non-empty (forgotten cleanup) → architectural debt is hidden. | Medium | The PR must remove the three entries (`SearchDocumentsRequest`, `SearchDocumentsResponse`, `ChunkResult`). Reviewer checklist item. The test will start *passing* with the allowlist tightened; if a developer forgets to tighten it, the test still passes but the boundary regresses silently. Add a one-line assertion `ArticleAllowlist.Should().BeEmpty()` is **not** worth the extra mechanism — the comment update is sufficient signal. |
| `IMediator` is removed from `GatherContextStep` but a later in-flight branch reintroduces it. | Low | Strictly out of scope. The two-line ctor diff is small and visible in review. |
| Adapter and downstream handler both throw on identical inputs differently (e.g. validation throws inside MediatR vs. in adapter input check). | Low | The adapter performs no validation; `SearchDocumentsRequest` validation attributes (`[Required, MinLength(1), MaxLength(2000)]` on `Query`, `[Range(1, 20)]` on `TopK`) continue to fire inside the MediatR pipeline. Behavior is bit-identical to today. |
| Article tests pin behavior on `Send(SearchDocumentsRequest, …)` being called once. | Low | Tests must be updated (FR-5). The single `Verify` assertion at `GatherContextStepTests.cs:80-82` becomes a `SearchAsync` verify on the new mock. |
| Spec's "remove IMediator if only used for KB search" condition mis-evaluated in implementation. | Low | Verified by reading the file: `_mediator` is used at exactly one site (line 88). Removal is safe. |
| Smartsupp follow-up not tracked. | Low | The spec already acknowledges Smartsupp is out of scope (FR-7). Capture as a separate issue at PR-creation time. |

## Specification Amendments

The spec is unusually faithful to the existing code and to the reference pattern. Only minor clarifications, none of which change scope:

1. **FR-2 folder placement is decided.** The spec hedges ("create `Infrastructure/` folder if it does not exist — verify against `filesystem.md` conventions"). Verified: `Features/KnowledgeBase/Infrastructure/` already exists and contains two analogous adapters. Place the file there with no further analysis.

2. **FR-2 adapter visibility is decided.** The spec hedges ("internal-visible if the existing style-guide adapter is, otherwise public sealed"). Verified: both reference adapters are `internal sealed`. Use `internal sealed`.

3. **FR-3 DI registration site is decided.** Add the line in `KnowledgeBaseModule.AddKnowledgeBaseModule` immediately after the existing `services.AddScoped<IArticleStyleGuideSource, KnowledgeBaseArticleStyleGuideSource>();` (line 43), preceded by a 2-line comment using the same wording style as lines 36-39 / 41-42 ("Cross-module contract: KnowledgeBase implements Article's IArticleKnowledgeSource via adapter. Same provider-owned-DI pattern as the bindings above.").

4. **FR-4 `IMediator` removal is decided, not conditional.** `_mediator` is used at exactly one site in `GatherContextStep`. Remove the field, the constructor parameter, and the `using MediatR;` import.

5. **FR-6 allowlist tightening is decided, not conditional.** Per `ModuleBoundariesTests.ArticleAllowlist`, all three current entries are the references being removed. Delete all three entries; keep the empty `HashSet` so the `Rules()` row still compiles. Update the prefacing comment from "Pre-existing dependency: GatherContextStep dispatches SearchDocumentsRequest..." to either a one-liner ("Empty — no active violations.") matching `PurchaseAllowlist` or removed entirely. **No new dedicated assertion is needed.**

6. **Add an additional acceptance criterion to FR-4**: the `using MediatR;` import in `GatherContextStep.cs` is removed when `IMediator` is dropped. (Currently implicit in FR-4 but worth being explicit since the file currently has both `using MediatR;` and `using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;` on consecutive lines.)

7. **Add a note to FR-5**: a 6th test should be added that asserts the adapter is invoked with the **exact** `topK` from `ArticleOptions.KnowledgeBaseTopK` — the existing `Times.Once` verify only checks invocation count. This is a one-line `It.Is<int>(k => k == _options.KnowledgeBaseTopK)` addition and protects against regression where the option is bypassed. *(Optional; reviewer call. If declined, leave as-is per FR-5 scope discipline.)*

## Prerequisites

None. No migrations, configuration changes, infrastructure provisioning, secrets, environment variables, feature flags, or schema work. The change is pure in-process code. `dotnet build`, `dotnet format`, and the existing test suite (`Anela.Heblo.Tests`) are the only validation needed.