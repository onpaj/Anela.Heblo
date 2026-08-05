# Architecture Review: Invert Smartsupp → KnowledgeBase dependency in GenerateDraftReplyHandler

## Skip Design: true

## Architectural Fit Assessment

This is a pure module-boundary-conformance fix with no behavioral, API, or UI surface change. It slots into a pattern this codebase has already applied twice (Article, Leaflet) and machine-enforces via `ModuleBoundariesTests`. I verified all three load-bearing claims in the spec against the actual code:

- `GenerateDraftReplyHandler.cs` (line 1) does `using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;` and dispatches `new SearchDocumentsRequest { Query = retrievalQuery, TopK = RetrievalTopK }` via `_mediator.Send` (line 70) — confirmed, `_mediator` (`IMediator`) has no other use in the handler, so it becomes fully removable.
- `IArticleKnowledgeSource` (`Application/Features/Article/Contracts/IArticleKnowledgeSource.cs`) is `Task<IReadOnlyList<ArticleKnowledgeChunk>> SearchAsync(string query, int topK, CancellationToken)`, and `ArticleKnowledgeChunk` has `ChunkId`, `SourceFilename`, `Content`, `Score` — the exact shape the spec extends with `DocumentId`. `KnowledgeBaseArticleKnowledgeSource` (`KnowledgeBase/Infrastructure/`) is `internal sealed`, injects only `IMediator`, and maps `ChunkResult` field-by-field. This is the class the new adapter should be a structural copy of.
- `ILeafletKnowledgeSource` (`Application/Features/Leaflet/Contracts/ILeafletKnowledgeSource.cs`) is **not** the same shape — it takes a pre-computed `float[] queryEmbedding`, not a `string query`, because Leaflet does its own embedding generation upstream. `docs/architecture/development_guidelines.md`'s "Cross-Module Communication Example" is titled after `ILeafletKnowledgeSource`, but its own text describes the *general* invert-dependency pattern (contract-in-consumer / adapter-in-provider / provider-owned DI), not this specific method signature. **Correction to note explicitly for the implementer:** copy `IArticleKnowledgeSource`'s signature, not `ILeafletKnowledgeSource`'s — the spec already gets this right ("closest existing precedent is `IArticleKnowledgeSource`"), but a developer skimming only the guidelines doc could be misled by the doc's Leaflet-titled heading into copying the wrong shape.
- `SearchDocumentsResponse.Chunks` is `List<ChunkResult>` with `ChunkId: Guid`, `DocumentId: Guid`, `Content: string`, `Score: double`, `SourceFilename: string`, `SourcePath: string`. The spec's `SmartsuppKnowledgeChunk` intentionally omits `SourcePath` — correct, `GenerateDraftReplyHandler`/`DraftReplySource` never reads it (`ArticleKnowledgeChunk` omits it too, for the same reason).
- `KnowledgeBaseModule.AddKnowledgeBaseModule` (`KnowledgeBase/KnowledgeBaseModule.cs`) is exactly where the Leaflet and Article bindings already live (`services.AddScoped<ILeafletKnowledgeSource, KnowledgeBaseLeafletSourceAdapter>()`, `services.AddScoped<IArticleKnowledgeSource, KnowledgeBaseArticleKnowledgeSource>()`), each with a one-line "Cross-module contract" comment — the new `ISmartsuppKnowledgeSource` binding belongs immediately after these, same comment convention, same `AddScoped` lifetime.
- `SmartsuppModule.cs` (`Application/Features/Smartsupp/SmartsuppModule.cs`) has no KnowledgeBase reference today and must gain none — it only needs `ISmartsuppKnowledgeSource` resolvable via constructor injection, which DI resolves regardless of which module registered it.
- `ModuleBoundariesTests.cs` confirmed: `LeafletAllowlist` and `ArticleAllowlist` (lines 22–27) are both empty `HashSet<string>`, each with a one-line "why empty" comment; the `Rules()` `TheoryData` entries for "Leaflet -> KnowledgeBase" and "Article -> KnowledgeBase" (lines 381–401) use identical `ForbiddenNamespacePrefixes` (`Domain.Features.KnowledgeBase`, `Application.Features.KnowledgeBase`, `Persistence.KnowledgeBase`) against `InspectedNamespacePrefix: "Anela.Heblo.Application.Features.Article"` / `"...Leaflet"`. The spec's proposed `"Smartsupp -> KnowledgeBase"` entry is a mechanical copy of this shape with `InspectedNamespacePrefix: "Anela.Heblo.Application.Features.Smartsupp"` — no corrections needed.
- `docs/architecture/module-map.md` §29 ("Customer Support (Smartsupp)") lists `**Depends on:** #46, #30.` — no KnowledgeBase (#not listed) dependency declared, confirming the brief's observation and reinforcing that this coupling was never sanctioned.
- `GenerateDraftReplyHandlerTests.cs` mocks `IMediator` directly and asserts on `SearchDocumentsRequest`/`ChunkResult` (`SetupSearch`, `CaptureSearch`, `_capturedSearch`). These tests must be rewritten to mock `ISmartsuppKnowledgeSource` instead — the spec's Out-of-Scope section already flags this as required-but-no-new-scenarios, which is correct; I list the concrete rework below since it's not optional to keep the suite green.

No corrections to the spec's functional requirements were needed — FR-1 through FR-6 as written match the codebase precisely. The one addition below (test rework detail, and the doc-heading caveat) closes gaps the spec left implicit.

## Proposed Architecture

### Component Overview

```
Smartsupp module (consumer)                    KnowledgeBase module (provider)
─────────────────────────────                  ───────────────────────────────
GenerateDraftReplyHandler                       KnowledgeBaseSmartsuppKnowledgeSource
  ctor(ISmartsuppKnowledgeSource _knowledgeSource)  : ISmartsuppKnowledgeSource
  -> _knowledgeSource.SearchAsync(query, topK, ct)    ctor(IMediator _mediator)
                                                       -> _mediator.Send(SearchDocumentsRequest)
Smartsupp.Contracts                                   -> maps ChunkResult -> SmartsuppKnowledgeChunk
  ISmartsuppKnowledgeSource (interface)
  SmartsuppKnowledgeChunk (DTO)                 KnowledgeBase.Infrastructure
                                                   (adapter lives here, internal sealed)
        ▲ implements                                  │
        └───────────────────────────────────────────────┘
                                                 KnowledgeBaseModule.AddKnowledgeBaseModule
                                                   services.AddScoped<ISmartsuppKnowledgeSource,
                                                                       KnowledgeBaseSmartsuppKnowledgeSource>()
```

Dependency arrow (compile-time): `Smartsupp.Infrastructure/Handler → Smartsupp.Contracts ← KnowledgeBase.Infrastructure`. KnowledgeBase depends on Smartsupp's contract namespace (acceptable — provider knows about consumer contracts it implements); Smartsupp never references KnowledgeBase. This is the same shape as the existing Article/Leaflet adapters, just one more spoke off the same KnowledgeBase hub.

### Key Design Decisions

#### Decision 1: Template the new contract on `IArticleKnowledgeSource`, not `ILeafletKnowledgeSource`
**Options considered:** (a) copy `ILeafletKnowledgeSource`'s embedding-based signature (`float[] queryEmbedding`), matching the guidelines doc's example title; (b) copy `IArticleKnowledgeSource`'s string-query signature; (c) invent a new shape.
**Chosen approach:** (b). `GenerateDraftReplyHandler` builds a plain-text retrieval query (`retrievalQuery`) and never computes an embedding itself — embedding generation happens inside `SearchDocumentsHandler`. This is exactly Article's situation, not Leaflet's (Leaflet pre-embeds elsewhere and searches by vector).
**Rationale:** Matching the actual call-site shape avoids introducing embedding-generation logic into Smartsupp that doesn't exist today and isn't asked for. `docs/architecture/development_guidelines.md`'s heading names Leaflet only because it was the first instance of the pattern; its prose is signature-agnostic. Recommend the implementer add a one-line comment on `ISmartsuppKnowledgeSource` noting it mirrors `IArticleKnowledgeSource`, not the guidelines doc's named Leaflet example, to prevent future confusion.

#### Decision 2: DTO carries `DocumentId` (unlike `ArticleKnowledgeChunk`)
**Options considered:** (a) reuse `ArticleKnowledgeChunk` as-is (no `DocumentId`) and have the handler look it up separately; (b) define a Smartsupp-owned `SmartsuppKnowledgeChunk` with `DocumentId` included; (c) add `DocumentId` to the shared `ArticleKnowledgeChunk` and reuse it across both modules.
**Chosen approach:** (b), as specified.
**Rationale:** `DraftReplySource.DocumentId` is part of the public `GenerateDraftReplyResponse` contract today (verified in `GenerateDraftReplyResponse.cs` / handler line 121) — dropping it would be a regression. (c) would couple Article's and Smartsupp's DTOs together for no shared benefit and contradicts the "no speculative methods/fields" principle in the guidelines doc — each consumer's contract should carry only what that consumer needs. Two small near-duplicate DTOs (`ArticleKnowledgeChunk`, `SmartsuppKnowledgeChunk`) is the correct cost of proper module isolation here, consistent with how the codebase already tolerates near-duplicate contract DTOs elsewhere (e.g. `LogisticsGiftPackageItem`/`LogisticsCatalogItem` patterns).

#### Decision 3: Adapter visibility and placement — `internal sealed` in `KnowledgeBase/Infrastructure/`
**Options considered:** (a) `public` class so it's directly unit-testable from outside; (b) `internal sealed` matching `KnowledgeBaseArticleKnowledgeSource`/`KnowledgeBaseLeafletSourceAdapter`.
**Chosen approach:** (b).
**Rationale:** Both existing adapters are `internal sealed`; DI resolves it via the interface so external testability isn't blocked (tests interact with `ISmartsuppKnowledgeSource`, not the concrete adapter). Consistency with the established pattern outweighs any marginal benefit of `public`.

## Implementation Guidance

### Directory / Module Structure
New files (exactly as spec FR-1/FR-2 state, both verified as the correct locations against the Article precedent):
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/Contracts/ISmartsuppKnowledgeSource.cs` — new `Contracts/` folder under Smartsupp (does not exist yet; Article's and Leaflet's `Contracts/` folders are the precedent for creating one here).
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/KnowledgeBaseSmartsuppKnowledgeSource.cs` — alongside `KnowledgeBaseArticleKnowledgeSource.cs` and `KnowledgeBaseLeafletSourceAdapter.cs` in the existing `Infrastructure/` folder.

Modified files:
- `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GenerateDraftReply/GenerateDraftReplyHandler.cs` — swap `IMediator`+`SearchDocumentsRequest` for `ISmartsuppKnowledgeSource` (remove the now-unused `IMediator` field/ctor param and the `using ...KnowledgeBase.UseCases.SearchDocuments;` import; add `using Anela.Heblo.Application.Features.Smartsupp.Contracts;`).
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs` — add the `ISmartsuppKnowledgeSource` binding directly after the existing `IArticleKnowledgeSource` binding (line ~46), same comment style.
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — add `SmartsuppKnowledgeBaseAllowlist` (empty) near `ArticleAllowlist`/`LeafletAllowlist` (after line 27), and a new `TheoryData` entry "Smartsupp -> KnowledgeBase" placed after the "Article -> KnowledgeBase" entry (after line 401), same three `ForbiddenNamespacePrefixes`.
- `backend/test/Anela.Heblo.Tests/Features/Smartsupp/GenerateDraftReplyHandlerTests.cs` — **must be reworked, not just left passing incidentally**: replace `Mock<IMediator> _mediator` with `Mock<ISmartsuppKnowledgeSource> _knowledgeSource`; `SetupSearch(params ChunkResult[])` becomes `SetupSearch(params SmartsuppKnowledgeChunk[])` mocking `_knowledgeSource.Setup(k => k.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(chunks)`; `CaptureSearch()`/`_capturedSearch` (currently capturing the `SearchDocumentsRequest` to assert on `.Query`/truncation, e.g. `Handle_TruncatesRetrievalQuery_ToSearchDocumentsMaxLength` at line 250 and `Handle_UsesTopicAsRetrievalQuery_WhenTopicProvided` at line 119) must instead capture the `query`/`topK` arguments passed to `SearchAsync` via a `Callback<string, int, CancellationToken>`; `CreateHandler()` (line 37) drops the `_mediator.Object` constructor argument and adds `_knowledgeSource.Object`; the `Chunk(...)` test helper (line 83) changes its return type from `ChunkResult` to `SmartsuppKnowledgeChunk`. No new test scenarios are needed — every existing assertion maps 1:1 onto the new mock surface.

### Interfaces and Contracts

```csharp
// Anela.Heblo.Application.Features.Smartsupp.Contracts (Smartsupp-owned)
public interface ISmartsuppKnowledgeSource
{
    Task<IReadOnlyList<SmartsuppKnowledgeChunk>> SearchAsync(
        string query, int topK, CancellationToken cancellationToken);
}

public class SmartsuppKnowledgeChunk
{
    public Guid ChunkId { get; set; }
    public Guid DocumentId { get; set; }
    public string SourceFilename { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public double Score { get; set; }
}
```

```csharp
// Anela.Heblo.Application.Features.KnowledgeBase.Infrastructure (KnowledgeBase-owned)
internal sealed class KnowledgeBaseSmartsuppKnowledgeSource : ISmartsuppKnowledgeSource
{
    private readonly IMediator _mediator;
    public KnowledgeBaseSmartsuppKnowledgeSource(IMediator mediator) => _mediator = mediator;

    public async Task<IReadOnlyList<SmartsuppKnowledgeChunk>> SearchAsync(
        string query, int topK, CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(
            new SearchDocumentsRequest { Query = query, TopK = topK }, cancellationToken);
        return response.Chunks
            .Select(c => new SmartsuppKnowledgeChunk
            {
                ChunkId = c.ChunkId,
                DocumentId = c.DocumentId,
                SourceFilename = c.SourceFilename,
                Content = c.Content,
                Score = c.Score,
            })
            .ToArray();
    }
}
```

This is the **only** class in the codebase permitted to reference both `ISmartsuppKnowledgeSource` and `SearchDocumentsRequest`/`ChunkResult` — it is the mapping boundary the new architecture test enforces.

### Data Flow

1. `GenerateDraftReplyHandler.Handle` builds `retrievalQuery` (topic or last-contact-messages, truncated to `MaxRetrievalQueryLength`) exactly as today.
2. Handler calls `_knowledgeSource.SearchAsync(retrievalQuery, RetrievalTopK, cancellationToken)` — a call against Smartsupp's own contract, resolved at runtime to `KnowledgeBaseSmartsuppKnowledgeSource`.
3. The adapter dispatches `SearchDocumentsRequest` via `IMediator.Send` — unchanged embedding/vector-search/similarity-threshold pipeline inside `SearchDocumentsHandler`.
4. The adapter maps `ChunkResult` → `SmartsuppKnowledgeChunk` (KnowledgeBase-owned shape → Smartsupp-owned shape) before returning across the module boundary.
5. Handler consumes `IReadOnlyList<SmartsuppKnowledgeChunk>` for prompt-context assembly and `DraftReplySource` mapping — same downstream logic, different input type name only.

No change to `SearchDocumentsHandler`, `IRagInteractionRecorder`, `RagFeature`, or the public `GenerateDraftReplyRequest`/`Response` contract.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Test rework (`GenerateDraftReplyHandlerTests.cs`) is more extensive than the spec's "no new scenarios" framing suggests — every test touching search mocking must change its mock target type | Low | Enumerated the exact required changes above; it's mechanical (same assertions, new mock surface), not a design risk. |
| A future developer copies `ILeafletKnowledgeSource`'s embedding-based signature instead of `IArticleKnowledgeSource`'s string-query signature, misled by the guidelines doc's Leaflet-titled heading | Low | Add a one-line XML-doc comment on `ISmartsuppKnowledgeSource` referencing `IArticleKnowledgeSource` as the structural template (Decision 1). Optionally file a docs follow-up to retitle/broaden the guidelines example — out of scope here. |
| `IMediator` removal from `GenerateDraftReplyHandler`'s constructor breaks any other test or caller relying on that specific constructor arity | Low | Confirmed via full-file read that `_mediator` has exactly one use site (the `SearchDocumentsRequest` dispatch); no other production or test code outside `GenerateDraftReplyHandlerTests.cs` constructs this handler directly (MediatR resolves it via DI). |
| New `ModuleBoundariesTests` rule could surface *other*, currently-unknown Smartsupp→KnowledgeBase references beyond `GenerateDraftReplyHandler.cs`, failing CI unexpectedly after FR-6 lands | Low | FR-6's acceptance criteria already call for adding the rule *before confirming it passes only after* FR-1–FR-4 land, which surfaces this at implementation time, not after merge. Grep for `Features.KnowledgeBase` under `Application/Features/Smartsupp/` before finalizing to confirm no other call sites exist (a broader grep than just this handler). |

## Specification Amendments

1. **FR-4 acceptance criteria** — add the concrete note that `Handle_UsesTopicAsRetrievalQuery_WhenTopicProvided` and `Handle_TruncatesRetrievalQuery_ToSearchDocumentsMaxLength` (which currently assert on `_capturedSearch!.Query`) must switch to capturing `SearchAsync`'s `query` argument directly, not `SearchDocumentsRequest.Query` — the spec's FR-4 already implies this but doesn't call out these two specific tests as needing a capture-mechanism change (not just a mock-type swap).
2. **Add to Implementation Guidance**: `ISmartsuppKnowledgeSource`'s XML doc comment should note it structurally mirrors `IArticleKnowledgeSource` (string-query shape), not `ILeafletKnowledgeSource` (embedding shape), even though the latter is the guidelines doc's named example — see Decision 1. This avoids a plausible future mis-copy.
3. No changes to FR-1, FR-2, FR-3, FR-5, FR-6, the Data Model, or Out-of-Scope sections — all verified correct against the current codebase.

## Prerequisites

None. No migrations, config, or infrastructure changes are needed — this is a same-assembly, same-deployment-unit refactor (KnowledgeBase and Smartsupp both live in `Anela.Heblo.Application` today; the module boundary is enforced by reflection tests, not physical assembly separation). Implementation can start immediately in a single PR covering FR-1 through FR-6 plus the test rework.
