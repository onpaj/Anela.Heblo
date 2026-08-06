# Design: Invert Smartsupp → KnowledgeBase dependency in GenerateDraftReplyHandler

## Component Design

### `ISmartsuppKnowledgeSource` (new, Smartsupp-owned contract)
- **Location:** `backend/src/Anela.Heblo.Application/Features/Smartsupp/Contracts/ISmartsuppKnowledgeSource.cs` (new `Contracts/` folder under Smartsupp — precedent: Article's and Leaflet's own `Contracts/` folders).
- **Responsibility:** Smartsupp-owned, read-only abstraction over "search the knowledge base for relevant chunks." Defines exactly the operation `GenerateDraftReplyHandler` needs; nothing more.
- **Structural template:** `IArticleKnowledgeSource` (string-query shape), **not** `ILeafletKnowledgeSource` (embedding-vector shape) — `GenerateDraftReplyHandler` builds a plain-text `retrievalQuery` and never computes an embedding itself, exactly like Article's call site. Include a one-line XML-doc note on the interface stating it mirrors `IArticleKnowledgeSource`, to prevent a future implementer from copying the Leaflet shape by mistake (the guidelines doc's example is titled after Leaflet even though its prose is signature-agnostic).
- **Interface contract:**
  ```csharp
  Task<IReadOnlyList<SmartsuppKnowledgeChunk>> SearchAsync(
      string query, int topK, CancellationToken cancellationToken);
  ```
- **Constraint:** must not reference any `Anela.Heblo.Domain.Features.KnowledgeBase`, `Anela.Heblo.Application.Features.KnowledgeBase`, or `Anela.Heblo.Persistence.KnowledgeBase` type — this file is machine-checked by the new `ModuleBoundariesTests` rule (see below).

### `KnowledgeBaseSmartsuppKnowledgeSource` (new, KnowledgeBase-owned adapter)
- **Location:** `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/KnowledgeBaseSmartsuppKnowledgeSource.cs`, alongside `KnowledgeBaseArticleKnowledgeSource.cs` and `KnowledgeBaseLeafletSourceAdapter.cs`.
- **Visibility:** `internal sealed`, matching both existing adapters. DI resolves it via the `ISmartsuppKnowledgeSource` interface, so external testability is not blocked — tests interact only with the interface, never the concrete class.
- **Responsibility:** Implements `ISmartsuppKnowledgeSource` by delegating to the existing `SearchDocumentsRequest`/`SearchDocumentsHandler` MediatR flow, unchanged, and maps the KnowledgeBase-owned `ChunkResult` shape to the Smartsupp-owned `SmartsuppKnowledgeChunk` shape.
- **This is the single mapping boundary class** in the codebase permitted to reference both `ISmartsuppKnowledgeSource` (Smartsupp.Contracts) and `SearchDocumentsRequest`/`SearchDocumentsResponse`/`ChunkResult` (KnowledgeBase.UseCases.SearchDocuments).
- **Dependencies:** constructor takes only `IMediator`.
- **Behavior:** no change to embedding generation, vector search, or similarity-threshold filtering inside `SearchDocumentsHandler` — one extra in-process call + DTO mapping per invocation.

### `GenerateDraftReplyHandler` (modified, Smartsupp)
- **Location (unchanged):** `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GenerateDraftReply/GenerateDraftReplyHandler.cs`.
- **Change:** constructor dependency swaps from `IMediator` (used only for the `SearchDocumentsRequest` dispatch) to `ISmartsuppKnowledgeSource _knowledgeSource`. `IMediator`/`_mediator` is removed entirely from the field list and constructor — confirmed to have no other use site in the handler.
- **Call site change:** `_mediator.Send(new SearchDocumentsRequest {...}, ct)` → `_knowledgeSource.SearchAsync(retrievalQuery, RetrievalTopK, cancellationToken)`, returning `IReadOnlyList<SmartsuppKnowledgeChunk>` in place of `SearchDocumentsResponse`.
- **Everything else preserved byte-for-byte in logic:** retrieval-query construction from topic/transcript, truncation via `MaxRetrievalQueryLength`, empty-conversation/empty-query error paths, chat-client prompt construction, `IRagInteractionRecorder.RecordInteraction` call, `DraftReplySource` mapping (including `Excerpt` truncation via `MaxExcerptLength`), and existing exception handling around `_chatClient.GetResponseAsync`. `RetrievalTopK` and `MaxRetrievalQueryLength` constants are unchanged.
- **Imports:** remove `using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;`; add `using Anela.Heblo.Application.Features.Smartsupp.Contracts;`.

### `KnowledgeBaseModule` (modified — DI wiring, provider-owned)
- **Location:** `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs`, inside `AddKnowledgeBaseModule`, directly after the existing `IArticleKnowledgeSource` binding.
- **New registration:**
  ```csharp
  // Cross-module contract: KnowledgeBase implements Smartsupp's ISmartsuppKnowledgeSource via adapter.
  // Same provider-owned-DI pattern as the Leaflet/Article bindings above.
  services.AddScoped<ISmartsuppKnowledgeSource, KnowledgeBaseSmartsuppKnowledgeSource>();
  ```
- **Lifetime:** `Scoped`, matching `ILeafletKnowledgeSource`/`IArticleKnowledgeSource`.
- `SmartsuppModule.AddSmartsuppModule` is **not** modified — it never registers this binding; it only needs `ISmartsuppKnowledgeSource` to be resolvable via constructor injection, which works regardless of which module owns the registration.

### `ModuleBoundariesTests` (modified — CI enforcement)
- **Location:** `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`.
- **New allowlist** (near `ArticleAllowlist`/`LeafletAllowlist`), empty:
  ```csharp
  // Allowlist for Smartsupp -> KnowledgeBase. Empty — GenerateDraftReplyHandler now consumes
  // the Smartsupp-owned ISmartsuppKnowledgeSource contract; the KnowledgeBase adapter
  // (KnowledgeBaseSmartsuppKnowledgeSource) lives in KnowledgeBase.Infrastructure.
  private static readonly HashSet<string> SmartsuppKnowledgeBaseAllowlist = new(StringComparer.Ordinal);
  ```
- **New `TheoryData<ModuleBoundaryRule>` entry** (placed after the existing "Article -> KnowledgeBase" entry):
  ```csharp
  new ModuleBoundaryRule(
      Name: "Smartsupp -> KnowledgeBase",
      InspectedNamespacePrefix: "Anela.Heblo.Application.Features.Smartsupp",
      ForbiddenNamespacePrefixes: new[]
      {
          "Anela.Heblo.Domain.Features.KnowledgeBase",
          "Anela.Heblo.Application.Features.KnowledgeBase",
          "Anela.Heblo.Persistence.KnowledgeBase",
      },
      Allowlist: SmartsuppKnowledgeBaseAllowlist),
  ```
- Runs as an additional case of the existing `Consumer_types_should_not_reference_provider_owned_namespaces` parameterized test — no new test method.
- Validation order: add this rule in isolation before FR-1–FR-4 land to confirm it fails (proves it detects the real violation), then confirm it passes once the handler is migrated and the allowlist stays empty.

### `GenerateDraftReplyHandlerTests` (modified — test double rework, mechanical)
- **Location:** `backend/test/Anela.Heblo.Tests/Features/Smartsupp/GenerateDraftReplyHandlerTests.cs`.
- `Mock<IMediator> _mediator` → `Mock<ISmartsuppKnowledgeSource> _knowledgeSource`.
- `SetupSearch(params ChunkResult[])` → `SetupSearch(params SmartsuppKnowledgeChunk[])`, backed by `_knowledgeSource.Setup(k => k.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(chunks)`.
- `CaptureSearch()`/`_capturedSearch` (previously capturing `SearchDocumentsRequest` to assert on `.Query`) → capture the `query`/`topK` arguments passed into `SearchAsync` via a `Callback<string, int, CancellationToken>`. Affects `Handle_UsesTopicAsRetrievalQuery_WhenTopicProvided` and `Handle_TruncatesRetrievalQuery_ToSearchDocumentsMaxLength`, which must assert on the captured `query` argument directly instead of `SearchDocumentsRequest.Query`.
- `CreateHandler()` drops the `_mediator.Object` constructor argument and adds `_knowledgeSource.Object`.
- `Chunk(...)` test helper changes its return type from `ChunkResult` to `SmartsuppKnowledgeChunk`.
- No new test scenarios required — every existing assertion maps 1:1 onto the new mock surface.

### Data flow (end to end)
1. `GenerateDraftReplyHandler.Handle` builds `retrievalQuery` (topic or last-contact-messages, truncated to `MaxRetrievalQueryLength`) — unchanged.
2. Handler calls `_knowledgeSource.SearchAsync(retrievalQuery, RetrievalTopK, cancellationToken)` against its own contract; DI resolves this at runtime to `KnowledgeBaseSmartsuppKnowledgeSource`.
3. The adapter dispatches `SearchDocumentsRequest` via `IMediator.Send` — unchanged embedding/vector-search/similarity-threshold pipeline inside `SearchDocumentsHandler`.
4. The adapter maps `ChunkResult` → `SmartsuppKnowledgeChunk` before returning across the module boundary.
5. Handler consumes `IReadOnlyList<SmartsuppKnowledgeChunk>` for prompt-context assembly and `DraftReplySource` mapping — same downstream logic, only the input type name changes.

No change to `SearchDocumentsHandler`, `IRagInteractionRecorder`, `RagFeature`, or the public `GenerateDraftReplyRequest`/`GenerateDraftReplyResponse` contract.

## Data Schemas

No persisted entities change. This is an internal, in-process contract refactor only — no database schema, no public HTTP API changes.

### New type: `ISmartsuppKnowledgeSource` (interface, Smartsupp-owned)
Namespace: `Anela.Heblo.Application.Features.Smartsupp.Contracts`
```csharp
public interface ISmartsuppKnowledgeSource
{
    Task<IReadOnlyList<SmartsuppKnowledgeChunk>> SearchAsync(
        string query, int topK, CancellationToken cancellationToken);
}
```

### New type: `SmartsuppKnowledgeChunk` (DTO, Smartsupp-owned)
Namespace: `Anela.Heblo.Application.Features.Smartsupp.Contracts`
```csharp
public class SmartsuppKnowledgeChunk
{
    public Guid ChunkId { get; set; }
    public Guid DocumentId { get; set; }
    public string SourceFilename { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public double Score { get; set; }
}
```
- Field-for-field subset of `ChunkResult` (`SearchDocumentsResponse.Chunks` element type), which additionally carries `SourcePath` — intentionally omitted since neither `GenerateDraftReplyHandler` nor `DraftReplySource` reads it (mirrors `ArticleKnowledgeChunk`'s same omission).
- Unlike `ArticleKnowledgeChunk`, includes `DocumentId` because `GenerateDraftReplyResponse.DraftReplySource.DocumentId` is part of today's public response contract and must not regress.

### New type: `KnowledgeBaseSmartsuppKnowledgeSource` (adapter class, KnowledgeBase-owned)
Namespace: `Anela.Heblo.Application.Features.KnowledgeBase.Infrastructure`
```csharp
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

### DI registration shape (KnowledgeBaseModule.AddKnowledgeBaseModule)
```csharp
services.AddScoped<ISmartsuppKnowledgeSource, KnowledgeBaseSmartsuppKnowledgeSource>();
```

### ModuleBoundariesTests allowlist/rule shape
```csharp
// Allowlist
private static readonly HashSet<string> SmartsuppKnowledgeBaseAllowlist = new(StringComparer.Ordinal);

// Rule entry (TheoryData<ModuleBoundaryRule>)
new ModuleBoundaryRule(
    Name: "Smartsupp -> KnowledgeBase",
    InspectedNamespacePrefix: "Anela.Heblo.Application.Features.Smartsupp",
    ForbiddenNamespacePrefixes: new[]
    {
        "Anela.Heblo.Domain.Features.KnowledgeBase",
        "Anela.Heblo.Application.Features.KnowledgeBase",
        "Anela.Heblo.Persistence.KnowledgeBase",
    },
    Allowlist: SmartsuppKnowledgeBaseAllowlist),
```

### Unchanged public contract
`GenerateDraftReplyRequest`/`GenerateDraftReplyResponse` (the MediatR contract exposed to the Smartsupp controller), `DraftReplySource`, `IRagInteractionRecorder`, `RagFeature`, and `SearchDocumentsRequest`/`SearchDocumentsHandler`/`ChunkResult` are all unchanged by this design.
