# Architecture Review: Decouple Article Module from KnowledgeBase Internals via Consumer-Owned Contract

## Skip Design: true

(Backend-only refactor — no UI components, screens, or visual decisions.)

## Architectural Fit Assessment

The proposal is an excellent fit. The codebase has a well-established, documented, and test-enforced pattern for cross-module read access: **consumer owns the contract, provider owns the adapter and the DI binding**. Two adapters already live in `Features/KnowledgeBase/Infrastructure/`:

- `KnowledgeBaseLeafletSourceAdapter` → `ILeafletKnowledgeSource` (in `Features/Leaflet/Contracts/`)
- `KnowledgeBaseArticleStyleGuideSource` → `IArticleStyleGuideSource` (in `Features/Article/Contracts/`)

Both are registered alongside each other in `KnowledgeBaseModule.AddKnowledgeBaseModule`, both are `internal sealed`, and the pattern is canonically described in `docs/architecture/development_guidelines.md` under "Cross-Module Communication Example: ILeafletKnowledgeSource". The architecture test `ModuleBoundariesTests.Consumer_types_should_not_reference_provider_owned_namespaces` already enforces the rule for `Article → KnowledgeBase` and currently carries a three-entry allowlist that explicitly tracks this exact violation (see `ModuleBoundariesTests.cs:50-61`).

Integration points are minimal: one new contract file (Article side), one new adapter file (KnowledgeBase side), one DI registration line (KnowledgeBase side), one constructor change + one method body change in `GatherContextStep`, plus test updates.

## Proposed Architecture

### Component Overview

```
┌─────────────────────────────────────────────────────────────┐
│ Features/Article (consumer)                                  │
│                                                              │
│   Contracts/                                                 │
│     ├─ IArticleStyleGuideSource         (existing)          │
│     ├─ IArticleUserResolver             (existing)          │
│     └─ IArticleKnowledgeSource          (NEW)               │
│         └─ ArticleKnowledgeChunk         (NEW DTO)          │
│                                                              │
│   UseCases/Generate/Pipeline/                                │
│     └─ GatherContextStep                                    │
│         • depends on IArticleKnowledgeSource (NEW)          │
│         • no longer imports KnowledgeBase.UseCases.*        │
└─────────────────────────────────────────────────────────────┘
                            ▲ interface
                            │ depends on
                            │
┌─────────────────────────────────────────────────────────────┐
│ Features/KnowledgeBase (provider)                            │
│                                                              │
│   Infrastructure/                                            │
│     ├─ KnowledgeBaseLeafletSourceAdapter        (existing)  │
│     ├─ KnowledgeBaseArticleStyleGuideSource     (existing)  │
│     └─ KnowledgeBaseArticleKnowledgeSource      (NEW)       │
│         • delegates to IMediator.Send(SearchDocumentsRequest)│
│         • projects ChunkResult → ArticleKnowledgeChunk      │
│                                                              │
│   KnowledgeBaseModule.cs                                     │
│     • services.AddScoped<IArticleKnowledgeSource, …>(); NEW  │
│                                                              │
│   UseCases/SearchDocuments/                                  │
│     ├─ SearchDocumentsRequest    (unchanged, internal)      │
│     ├─ SearchDocumentsResponse   (unchanged, internal)      │
│     └─ SearchDocumentsHandler    (unchanged)                │
└─────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Adapter delegates via MediatR rather than calling the handler internals directly
**Options considered:**
- (A) Adapter dispatches `SearchDocumentsRequest` through `IMediator` (per spec).
- (B) Adapter injects the underlying primitives directly (`IKnowledgeBaseRepository`, `IEmbeddingGenerator`, `IRagQueryExpander`, `KnowledgeBaseOptions`) and reimplements the search.
- (C) Extract a `IKnowledgeBaseSearchService` inside KB and have both the MediatR handler and the adapter call it.

**Chosen approach:** (A) — match the spec.

**Rationale:** `SearchDocumentsHandler` is non-trivial: it runs RAG query expansion, embedding generation, similarity threshold filtering, and transient-failure handling. Reimplementing that in the adapter (option B) duplicates logic and risks behavioral drift. Option C is cleaner long-term but is out of scope for a coupling fix. Going through MediatR keeps a single source of truth and is consistent with how `KnowledgeBaseLeafletSourceAdapter` calls `IKnowledgeBaseRepository` (one shared underlying primitive) — the adapter is intentionally thin.

#### Decision 2: Adapter visibility — `internal sealed`, not `public sealed`
**Options considered:**
- (A) `public sealed` (per spec).
- (B) `internal sealed` (matches existing pattern).

**Chosen approach:** (B) `internal sealed`.

**Rationale:** Both existing adapters (`KnowledgeBaseLeafletSourceAdapter`, `KnowledgeBaseArticleStyleGuideSource`) are `internal sealed`. There is no reason for the adapter type to be visible outside the KnowledgeBase module — only its interface contract is consumed externally. The spec's `public sealed` would break the established convention. **This is a spec amendment.**

#### Decision 3: Adapter naming — `KnowledgeBaseArticleKnowledgeSource`, not `KnowledgeBaseArticleSourceAdapter`
**Options considered:**
- (A) `KnowledgeBaseArticleSourceAdapter` (per spec).
- (B) `KnowledgeBaseArticleKnowledgeSource` (mirrors `KnowledgeBaseArticleStyleGuideSource`).
- (C) `KnowledgeBaseArticleKnowledgeSourceAdapter` (verbose, mixed pattern).

**Chosen approach:** (B) `KnowledgeBaseArticleKnowledgeSource`.

**Rationale:** The Article module will now have **two** KB-implemented contracts (`IArticleStyleGuideSource`, `IArticleKnowledgeSource`). The spec's `KnowledgeBaseArticleSourceAdapter` is ambiguous — it does not say *which* Article-facing source it implements. The existing sibling adapter for the style guide drops the "Adapter" suffix entirely and uses the `KnowledgeBase{Consumer}{Capability}Source` form. Matching that gives `KnowledgeBaseArticleKnowledgeSource`. (The Leaflet adapter uses the `Adapter` suffix, so the codebase is mixed — but within the Article-facing family, consistency is more valuable.) **This is a spec amendment.**

#### Decision 4: Architecture test — remove allowlist entries; do not add a new test
**Options considered:**
- (A) Add a brand new test as FR-6 says.
- (B) Delete the three Article-allowlist entries (`ModuleBoundariesTests.cs:58-60`) and let the existing `Article -> KnowledgeBase` rule fail on the slightest regression.

**Chosen approach:** (B).

**Rationale:** FR-6's behavior is **already implemented** by `ModuleBoundariesTests.Consumer_types_should_not_reference_provider_owned_namespaces`. It enforces a namespace-prefix ban for every type under `Anela.Heblo.Application.Features.Article`, forbids the entire `Anela.Heblo.Application.Features.KnowledgeBase` prefix (which subsumes `…UseCases.SearchDocuments`), and produces a clear failure message naming the offending caller. The only thing keeping the current violation green is the three-entry allowlist at `ModuleBoundariesTests.cs:58-60`. Deleting those entries is sufficient — and it is exactly what the existing comment instructs (*"Remove these three entries when SearchDocumentsRequest is replaced by an Article-owned contract."*). **This is a spec amendment.**

#### Decision 5: DTO style — class with public setters, but consider `init`
**Options considered:**
- (A) Public mutable setters (per spec, matches `ChunkResult`).
- (B) `init`-only setters (matches sibling `KnowledgeSearchResult` in Leaflet contracts).

**Chosen approach:** (A) with `init` as an acceptable alternative — either is fine.

**Rationale:** Project rule says DTOs are classes (not records); both setter styles satisfy that. `KnowledgeSearchResult` already uses `init`. The OpenAPI generator caveat applies to *records* with positional constructors, not to `init`-only properties on classes. Implementer's choice; the spec's mutable setters are safe.

## Implementation Guidance

### Directory / Module Structure

**New files:**
```
backend/src/Anela.Heblo.Application/Features/Article/Contracts/
  └─ IArticleKnowledgeSource.cs        # interface + ArticleKnowledgeChunk DTO

backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/
  └─ KnowledgeBaseArticleKnowledgeSource.cs   # internal sealed adapter
```

**Modified files:**
```
backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/GatherContextStep.cs
  • Drop:  using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;
  • Add:   IArticleKnowledgeSource _knowledgeSource ctor parameter + field
  • Keep:  IMediator (still unused by KB path, but no other consumer either —
           SEE OPEN ITEM: remove it if no remaining branch uses it)

backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs
  • Add:   services.AddScoped<IArticleKnowledgeSource, KnowledgeBaseArticleKnowledgeSource>();
           (place adjacent to the existing IArticleStyleGuideSource registration at line 43)

backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
  • Delete lines 58–60 (the three ArticleAllowlist entries).
  • Optionally delete the comment block at lines 52–57 since the rationale is gone.

backend/test/Anela.Heblo.Tests/Article/Pipeline/GatherContextStepTests.cs
  • Swap Mock<IMediator> KB setup → Mock<IArticleKnowledgeSource>.
  • Update CreateStep to pass the new mock.
  • IMediator mock may still be needed if IMediator remains in the constructor.

backend/test/Anela.Heblo.Tests/KnowledgeBase/  (new test file)
  └─ KnowledgeBaseArticleKnowledgeSourceTests.cs  # adapter round-trip test
```

### Interfaces and Contracts

```csharp
// Features/Article/Contracts/IArticleKnowledgeSource.cs
namespace Anela.Heblo.Application.Features.Article.Contracts;

/// <summary>
/// Article-owned read-only abstraction over the knowledge-base search index.
/// Implemented by the KnowledgeBase module via an adapter.
/// </summary>
public interface IArticleKnowledgeSource
{
    Task<IReadOnlyList<ArticleKnowledgeChunk>> SearchAsync(
        string query, int topK, CancellationToken cancellationToken);
}

public class ArticleKnowledgeChunk
{
    public Guid ChunkId { get; set; }
    public string SourceFilename { get; set; } = "";
    public string Content { get; set; } = "";
    public double Score { get; set; }
}
```

Naming alignment: parameter name should be `cancellationToken` (full word), matching the two existing sibling contracts.

```csharp
// Features/KnowledgeBase/Infrastructure/KnowledgeBaseArticleKnowledgeSource.cs
using Anela.Heblo.Application.Features.Article.Contracts;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;
using MediatR;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Infrastructure;

internal sealed class KnowledgeBaseArticleKnowledgeSource : IArticleKnowledgeSource
{
    private readonly IMediator _mediator;

    public KnowledgeBaseArticleKnowledgeSource(IMediator mediator) => _mediator = mediator;

    public async Task<IReadOnlyList<ArticleKnowledgeChunk>> SearchAsync(
        string query, int topK, CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(
            new SearchDocumentsRequest { Query = query, TopK = topK }, cancellationToken);

        return response.Chunks
            .Select(c => new ArticleKnowledgeChunk
            {
                ChunkId = c.ChunkId,
                SourceFilename = c.SourceFilename,
                Content = c.Content,
                Score = c.Score,
            })
            .ToArray();
    }
}
```

### Data Flow

**Before (current):**
```
GatherContextStep
  → IMediator.Send(SearchDocumentsRequest)
       → SearchDocumentsHandler (RAG expand → embed → repo search → threshold filter)
  → SearchDocumentsResponse.Chunks (List<ChunkResult>)
  → projects to List<ContextSnippet>
```

**After (proposed):**
```
GatherContextStep
  → IArticleKnowledgeSource.SearchAsync(query, topK, ct)
       → KnowledgeBaseArticleKnowledgeSource (adapter, KB module)
            → IMediator.Send(SearchDocumentsRequest)   ← unchanged from here down
                 → SearchDocumentsHandler
            → projects ChunkResult → ArticleKnowledgeChunk
  → IReadOnlyList<ArticleKnowledgeChunk>
  → projects to List<ContextSnippet>
```

End-user behavior is unchanged. Threshold filtering, transient-failure handling, and `BelowThresholdCount` reporting all still happen inside `SearchDocumentsHandler`; the adapter is a pure type-translation layer.

### `GatherContextStep` rewrite — the KB branch

```csharp
private async Task<List<ContextSnippet>> GatherKnowledgeBaseSnippetsAsync(
    List<string> queries, CancellationToken ct)
{
    var snippets = new List<ContextSnippet>();

    foreach (var query in queries)
    {
        try
        {
            var chunks = await _knowledgeSource.SearchAsync(query, _options.KnowledgeBaseTopK, ct);

            snippets.AddRange(chunks.Select(chunk => new ContextSnippet
            {
                Source = SourceType.KnowledgeBase,
                Title = chunk.SourceFilename,
                Excerpt = chunk.Content,
                Url = null,
                ChunkId = chunk.ChunkId,
                Score = chunk.Score
            }));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "KB search failed for query '{Query}'", query);
        }
    }

    return snippets;
}
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `IMediator` left dangling on `GatherContextStep` constructor after KB path migrates away. | Low | Inspect the file: the KB branch is the only consumer of `_mediator` in `GatherContextStep` (style guide uses `_styleGuideSource`, web uses `_webSearch`, recorder uses its own dep). Remove `IMediator` from the constructor. **Add this as an explicit acceptance criterion in FR-4** (spec already mentions it but should be assertive: "remove `IMediator`"). |
| Allowlist entries in `ModuleBoundariesTests.cs:58-60` left in place — refactor done but regression test still permits the old coupling. | Medium | Make "delete the three Article allowlist entries" an explicit subtask. CI will not catch missing deletions because the test still passes; the deletion is the *test*. |
| Adapter name collides conceptually with the existing `KnowledgeBaseArticleStyleGuideSource` (both adapt Article-side contracts). | Low | Use `KnowledgeBaseArticleKnowledgeSource` (matches the StyleGuideSource naming form). |
| Spec says `public sealed` but existing sibling adapters are `internal sealed`. Following the spec literally would inconsistently expose the adapter class. | Low | Use `internal sealed`. Codified in Decision 2. |
| Adapter delegates via `IMediator`, which means the test for the adapter must mock `IMediator` and assert request shape — fragile if `SearchDocumentsRequest` properties evolve. | Low | Acceptable: mocking `IMediator.Send` and inspecting the `SearchDocumentsRequest` captured by `It.Is<>` matches the test style used elsewhere; the test should be tolerant (assert `Query` and `TopK` only, not extra fields). |
| `SearchDocumentsHandler` filters by `MinSimilarityScore` and reports `BelowThresholdCount`. The new contract drops `BelowThresholdCount`. | Low | `GatherContextStep` does not read `BelowThresholdCount` today, so no behavioral change. Document this as an explicit non-regression. |
| Other modules might re-introduce direct `SearchDocumentsRequest` usage (Smartsupp is already a known offender). | Medium (scope) | Out of scope per spec — but `ModuleBoundariesTests` does not yet cover `Smartsupp → KnowledgeBase`. Flag for the follow-up. |

## Specification Amendments

The spec is high-quality and complete. The following clarifications/changes should be applied:

1. **Adapter visibility: change `public sealed` → `internal sealed`** (FR-2). Matches existing `KnowledgeBaseLeafletSourceAdapter` and `KnowledgeBaseArticleStyleGuideSource`. No external consumer requires a public adapter type.

2. **Adapter name: change `KnowledgeBaseArticleSourceAdapter` → `KnowledgeBaseArticleKnowledgeSource`** (FR-2). Disambiguates from `KnowledgeBaseArticleStyleGuideSource`, the other Article-side adapter living in the same folder.

3. **FR-6 is already satisfied by the existing test** — restate the work as:
   > Delete the three Article-allowlist entries at `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs:58-60` (and optionally the explanatory comment block at lines 52–57). The existing `Consumer_types_should_not_reference_provider_owned_namespaces[Article -> KnowledgeBase]` theory then enforces FR-6's intent automatically. **No new test is required.**

4. **FR-4: make `IMediator` removal assertive, not conditional.** Inspection confirms `GatherContextStep`'s KB branch is the sole `_mediator` consumer. Spec wording "retained only if still used" should become: "`IMediator` must be removed from the constructor; verify no remaining branch sends MediatR requests."

5. **Contract parameter naming:** the cancellation-token parameter on `IArticleKnowledgeSource.SearchAsync` should be named `cancellationToken` (full word), matching `IArticleStyleGuideSource.DownloadStyleGuideTextAsync` and `ILeafletKnowledgeSource.SearchSimilarAsync`. The spec's `ct` shorthand is inconsistent with project conventions.

6. **DI registration placement** (FR-3): explicitly place the new `services.AddScoped<IArticleKnowledgeSource, …>()` line immediately after the existing `IArticleStyleGuideSource` registration at `KnowledgeBaseModule.cs:43`, with a one-line comment mirroring the existing cross-module-contract comments at lines 36-43.

7. **Adapter test placement** (FR-5): the new `KnowledgeBaseArticleKnowledgeSourceTests` should live in `backend/test/Anela.Heblo.Tests/KnowledgeBase/` — there is currently no folder for KB-adapter tests, so create one. Verify by checking sibling test layout before final placement.

## Prerequisites

None. All required infrastructure is already in place:

- `Features/KnowledgeBase/Infrastructure/` folder exists and already hosts two analogous adapters.
- `Features/Article/Contracts/` folder exists and already hosts two analogous contracts (`IArticleStyleGuideSource`, `IArticleUserResolver`).
- `KnowledgeBaseModule.cs` already wires two cross-module bindings — slotting in a third is trivial.
- `ModuleBoundariesTests` already enforces the boundary; the work is to **remove** allowlist entries, not to add tests.
- No migrations, configuration, or infrastructure changes required.
- No dependent feature flags.

Implementation can begin immediately.