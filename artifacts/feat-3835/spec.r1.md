# Specification: Invert Smartsupp → KnowledgeBase dependency in GenerateDraftReplyHandler

## Summary
`GenerateDraftReplyHandler` (Smartsupp module) currently imports and directly dispatches KnowledgeBase's own `SearchDocumentsRequest`/`SearchDocumentsResponse` MediatR contract, violating the module-boundary rule already enforced for Leaflet and Article. This fix inverts the dependency by introducing a Smartsupp-owned `ISmartsuppKnowledgeSource` contract, implementing it as a KnowledgeBase-owned adapter, and adding a machine-enforced `Smartsupp → KnowledgeBase` rule to `ModuleBoundariesTests` — mirroring the pattern already shipped for `IArticleKnowledgeSource` (issue #1942) and `ILeafletKnowledgeSource`.

## Background
A daily architecture-review routine flagged (issue #3835) that `GenerateDraftReplyHandler.cs` references `Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments.SearchDocumentsRequest`/`SearchDocumentsResponse` directly via `IMediator.Send`, and consumes the KnowledgeBase-owned `ChunkResult` shape (`searchResult.Chunks`). This is the same class of violation fixed for Article's `GatherContextStep` in issue #1942, and for Leaflet earlier — both now documented in `docs/architecture/development_guidelines.md` under **Cross-Module Communication Example: `ILeafletKnowledgeSource`** and enforced by empty "Leaflet → KnowledgeBase" / "Article → KnowledgeBase" allowlists in `ModuleBoundariesTests`. Smartsupp has no corresponding rule at all, so this — and any future regression in the same file — goes undetected by CI. KnowledgeBase is also not a declared dependency of Smartsupp in the module map, reinforcing that the coupling must be removed, not merely allow-listed.

The fix must follow the codebase's already-established, twice-proven pattern:
1. Consumer module (Smartsupp) defines an interface in its own `Contracts/` folder exposing only the operations it needs.
2. Provider module (KnowledgeBase) implements that interface via an adapter in its `Infrastructure/` folder, delegating internally to the existing `SearchDocumentsRequest`/`SearchDocumentsHandler` MediatR flow.
3. Provider module registers the DI binding in `KnowledgeBaseModule.AddKnowledgeBaseModule`.
4. A new `ModuleBoundariesTests` rule (`Smartsupp → KnowledgeBase`, empty allowlist) makes the boundary CI-enforced going forward.

The closest existing precedent is `IArticleKnowledgeSource` (Article module), which already wraps `SearchDocumentsRequest` with a `string query, int topK` signature — the same shape `GenerateDraftReplyHandler` needs. The new Smartsupp contract mirrors that pattern, extended with `DocumentId` since `GenerateDraftReplyResponse.DraftReplySource` (unlike Article's DTO) surfaces it to the caller.

## Functional Requirements

### FR-1: Define `ISmartsuppKnowledgeSource` contract owned by Smartsupp
Add a new interface in `backend/src/Anela.Heblo.Application/Features/Smartsupp/Contracts/ISmartsuppKnowledgeSource.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Smartsupp.Contracts;

/// <summary>
/// Smartsupp-owned read-only abstraction over the knowledge-base search index.
/// Implemented by the KnowledgeBase module via an adapter.
/// </summary>
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

**Acceptance criteria:**
- The interface and DTO live under `Anela.Heblo.Application.Features.Smartsupp.Contracts` (Smartsupp-owned namespace), not in a shared/global location.
- `SmartsuppKnowledgeChunk` carries exactly the fields `GenerateDraftReplyHandler` consumes today: `ChunkId`, `DocumentId`, `SourceFilename`, `Content`, `Score` (matches `DraftReplySource`'s existing shape one-for-one, so the handler's mapping logic is otherwise unchanged).
- No KnowledgeBase namespace (`Anela.Heblo.Domain.Features.KnowledgeBase`, `Anela.Heblo.Application.Features.KnowledgeBase`, `Anela.Heblo.Persistence.KnowledgeBase`) appears anywhere in this file.

### FR-2: Implement the contract as a KnowledgeBase-owned adapter
Add `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/KnowledgeBaseSmartsuppKnowledgeSource.cs`, structurally identical to `KnowledgeBaseArticleKnowledgeSource`:

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

**Acceptance criteria:**
- Class lives in `Anela.Heblo.Application.Features.KnowledgeBase.Infrastructure` and is `internal sealed`, matching `KnowledgeBaseArticleKnowledgeSource`'s visibility/placement.
- It is the only class in the codebase permitted to reference both `ISmartsuppKnowledgeSource` (Smartsupp.Contracts) and `SearchDocumentsRequest`/`SearchDocumentsResponse` (KnowledgeBase.UseCases.SearchDocuments) — it is the mapping boundary.
- Internally dispatches to the existing `SearchDocumentsRequest` via `IMediator.Send`, with no change to `SearchDocumentsHandler` or its retrieval/embedding/RAG-recording behavior.
- Field-for-field mapping from `ChunkResult` to `SmartsuppKnowledgeChunk` preserves all five values with no loss (in particular `DocumentId`, which `IArticleKnowledgeSource`'s DTO omits but Smartsupp's does not).

### FR-3: Register the DI binding on the KnowledgeBase (provider) side
In `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs`, add, alongside the existing Leaflet/Article contract bindings:

```csharp
// Cross-module contract: KnowledgeBase implements Smartsupp's ISmartsuppKnowledgeSource via adapter.
// Same provider-owned-DI pattern as the Leaflet/Article bindings above.
services.AddScoped<ISmartsuppKnowledgeSource, KnowledgeBaseSmartsuppKnowledgeSource>();
```

**Acceptance criteria:**
- Registration is added inside `AddKnowledgeBaseModule`, not in `SmartsuppModule` or `Program.cs` — the provider module owns the binding, per the documented pattern.
- Scoped lifetime, matching `ILeafletKnowledgeSource` and `IArticleKnowledgeSource` registrations.
- `SmartsuppModule.AddSmartsuppModule` is not modified to register this binding (Smartsupp only consumes the interface via constructor injection).

### FR-4: Update `GenerateDraftReplyHandler` to consume the new contract
Modify `backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GenerateDraftReply/GenerateDraftReplyHandler.cs`:
- Remove the `using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;` import.
- Add `using Anela.Heblo.Application.Features.Smartsupp.Contracts;`.
- Replace the `ISmartsuppRepository`/`IMediator`/... constructor dependency on raw KnowledgeBase search with an injected `ISmartsuppKnowledgeSource _knowledgeSource`.
- Replace the call site:
  ```csharp
  var searchResult = await _mediator.Send(
      new SearchDocumentsRequest { Query = retrievalQuery, TopK = RetrievalTopK },
      cancellationToken);
  ```
  with:
  ```csharp
  var chunks = await _knowledgeSource.SearchAsync(retrievalQuery, RetrievalTopK, cancellationToken);
  ```
- Update the two downstream usages (`searchResult.Chunks.Count`, `searchResult.Chunks.Select(...)`) to operate on `chunks` (an `IReadOnlyList<SmartsuppKnowledgeChunk>`) instead of `searchResult.Chunks`.

**Acceptance criteria:**
- `GenerateDraftReplyHandler.cs` no longer references any `Anela.Heblo.Application.Features.KnowledgeBase.*`, `Anela.Heblo.Domain.Features.KnowledgeBase.*`, or `Anela.Heblo.Persistence.KnowledgeBase.*` type, directly or transitively through its own members (verified by the new architecture test in FR-6).
- `_mediator` (`IMediator`) is removed from the constructor if, after this change, it has no other remaining use in the handler; if another call site in the same handler still legitimately needs `IMediator` for an unrelated purpose, it is kept and only the KnowledgeBase-specific dispatch is replaced. (Inspection of the current handler shows `_mediator.Send` is used only for the `SearchDocumentsRequest` call, so `IMediator` becomes unused and should be removed from the constructor and field list.)
- All other handler behavior (topic/transcript retrieval-query construction, truncation to `MaxRetrievalQueryLength`, empty-conversation/empty-query error paths, chat-client prompt construction, `IRagInteractionRecorder.RecordInteraction` call, `DraftReplySource` mapping including the `Excerpt` truncation via `MaxExcerptLength`, and existing exception handling around `_chatClient.GetResponseAsync`) is preserved byte-for-byte in logic — only the retrieval call and its result type change.
- `RetrievalTopK` and `MaxRetrievalQueryLength` constants are unchanged and still apply to the new `SearchAsync` call exactly as they did to the old `SearchDocumentsRequest`.

### FR-5: Update `SmartsuppModule` DI wiring
No new registration is needed in `SmartsuppModule.cs` for `ISmartsuppKnowledgeSource` (bound by KnowledgeBase per FR-3), but confirm `GenerateDraftReplyHandler`'s constructor change is compatible with existing DI resolution (MediatR handler auto-registration + the module's existing `IPipelineBehavior<GenerateDraftReplyRequest, GenerateDraftReplyResponse>` registrations for `DraftReplyLoggingBehavior`).

**Acceptance criteria:**
- `dotnet build` succeeds with no new/changed DI registrations required in `SmartsuppModule.cs`.
- `DraftReplyLoggingBehavior` and its existing tests are unaffected (it observes the response after `Handle` completes and does not depend on how retrieval is performed).

### FR-6: Add a `Smartsupp → KnowledgeBase` module-boundary rule
In `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`:
- Add a new empty allowlist, following the existing naming/comment convention:
  ```csharp
  // Allowlist for Smartsupp -> KnowledgeBase. Empty — GenerateDraftReplyHandler now consumes
  // the Smartsupp-owned ISmartsuppKnowledgeSource contract; the KnowledgeBase adapter
  // (KnowledgeBaseSmartsuppKnowledgeSource) lives in KnowledgeBase.Infrastructure.
  private static readonly HashSet<string> SmartsuppKnowledgeBaseAllowlist = new(StringComparer.Ordinal);
  ```
- Add a new entry to the `Rules()` `TheoryData<ModuleBoundaryRule>`:
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
  placed alongside the existing "Leaflet → KnowledgeBase" and "Article → KnowledgeBase" rules for readability.

**Acceptance criteria:**
- The new theory case runs as part of the existing `Consumer_types_should_not_reference_provider_owned_namespaces` parameterized test — no new test method is added.
- With the allowlist empty, the test passes after FR-1–FR-4 are applied (i.e., zero violations found for the Smartsupp namespace against the three forbidden KnowledgeBase prefixes).
- Before FR-1–FR-4 are applied, this rule (added in isolation) would fail, confirming it actually detects the violation it's meant to guard against.

## Non-Functional Requirements

### NFR-1: Performance
No behavioral or performance change. The adapter is a direct pass-through to the existing `SearchDocumentsRequest`/`SearchDocumentsHandler` MediatR pipeline (same embedding generation, vector search, and similarity-threshold filtering as today) — one extra in-process method call and DTO mapping per draft-reply generation, negligible relative to the embedding/LLM calls already on this path.

### NFR-2: Security
No change. No new data exposure: `SmartsuppKnowledgeChunk` carries the same fields already returned to callers via `DraftReplySource`. No new authentication/authorization surface.

## Data Model
No persisted entities change. New in-memory contract types only:
- `Anela.Heblo.Application.Features.Smartsupp.Contracts.ISmartsuppKnowledgeSource` (interface)
- `Anela.Heblo.Application.Features.Smartsupp.Contracts.SmartsuppKnowledgeChunk` (DTO: `ChunkId: Guid`, `DocumentId: Guid`, `SourceFilename: string`, `Content: string`, `Score: double`)

## API / Interface Design
No public HTTP API changes. This is an internal module-boundary refactor:
- New consumer-owned interface: `ISmartsuppKnowledgeSource.SearchAsync(string query, int topK, CancellationToken) : Task<IReadOnlyList<SmartsuppKnowledgeChunk>>`
- New provider-owned adapter: `KnowledgeBaseSmartsuppKnowledgeSource : ISmartsuppKnowledgeSource`
- `GenerateDraftReplyRequest`/`GenerateDraftReplyResponse` (the MediatR contract exposed to the `Smartsupp` controller) are unchanged.

## Dependencies
- Existing `SearchDocumentsRequest`/`SearchDocumentsHandler` (KnowledgeBase.UseCases.SearchDocuments) — reused unmodified by the new adapter.
- Existing precedent code: `IArticleKnowledgeSource` / `KnowledgeBaseArticleKnowledgeSource` (Article module) is the direct structural template for this change.
- `ModuleBoundariesTests.cs` — extended, not restructured.

## Out of Scope
- Any change to `SearchDocumentsRequest`, `SearchDocumentsHandler`, embedding generation, vector search, or similarity thresholds.
- Any change to `GenerateDraftReplyRequest`/`GenerateDraftReplyResponse` public shape, `DraftReplySource`, or the Smartsupp HTTP controller/API contract.
- Any change to `IRagInteractionRecorder`, `RagFeature`, or other `Application.Shared.Rag` / `Domain.Features.Rag` types — these are shared kernel types already used across RAG-consuming modules (Leaflet, Article, KnowledgeBase, Smartsupp) and are not part of the flagged violation.
- Other Smartsupp features (`ListConversations`, `SendMessage`, `ProcessWebhookEvent`, presence, feedback) — untouched.
- Other KnowledgeBase consumers (Leaflet, Article) — their existing adapters and allowlists are not modified.
- Declaring Smartsupp's dependency on KnowledgeBase in the architecture module map / `docs/📘 Architecture Documentation` — noted as a documentation gap in the brief but not part of this code-level fix; the inverted-dependency pattern itself (consumer owns contract, provider owns adapter + DI) is what makes the module boundary correct regardless of map annotation.
- Adding new test coverage beyond what's needed to keep `backend/test/Anela.Heblo.Tests/Features/Smartsupp/GenerateDraftReplyHandlerTests.cs` passing against the new `ISmartsuppKnowledgeSource`-based constructor (existing tests must be updated to mock `ISmartsuppKnowledgeSource` instead of `IMediator`'s `SearchDocumentsRequest` dispatch, but no new test scenarios are required by this fix).

## Open Questions
None.

## Status: COMPLETE
