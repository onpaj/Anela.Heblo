## Evidence

`backend/src/Anela.Heblo.Application/Features/Smartsupp/UseCases/GenerateDraftReply/GenerateDraftReplyHandler.cs:1` imports `Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments`, and at line 70 dispatches KnowledgeBase's own use-case request directly:

```csharp
var searchResult = await _mediator.Send(
    new SearchDocumentsRequest { Query = retrievalQuery, TopK = RetrievalTopK },
    cancellationToken);
// then consumes searchResult.Chunks (KnowledgeBase-owned response type)
```

## Rule violated

`docs/architecture/development_guidelines.md` — **Module Communication** / **Cross-Module Communication Example: `ILeafletKnowledgeSource`**:

> "When module A needs **read-only access** to data in module B, the dependency must **invert**: the consumer owns the contract, the provider implements an adapter."

This is machine-enforced by `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` ("Consumer modules must not reference provider-owned types directly"). The **Leaflet → KnowledgeBase** and **Article → KnowledgeBase** allowlists are kept *empty* ("all violations fixed", lines 22–27). Smartsupp is the only non-KnowledgeBase feature still importing `Anela.Heblo.Application.Features.KnowledgeBase.*`, and no `Smartsupp → KnowledgeBase` rule exists, so it is unguarded.

This is the same violation already fixed for Article in closed issue #1942 ("Article: GatherContextStep directly imports SearchDocumentsRequest from KnowledgeBase's UseCase folder (module boundary violation)", `feat:done`) — this occurrence is a different file in a different module.

## Why it matters

Smartsupp has a compile-time dependency on KnowledgeBase's internal `SearchDocumentsRequest`/`SearchDocumentsResponse` contract. Changes to those types ripple into Smartsupp; the module cannot be extracted independently ("each module must be deployable as a separate microservice"); and the violation evades the boundary test that keeps Leaflet and Article clean, so future Smartsupp regressions go undetected. Note KnowledgeBase is not even a declared dependency of Smartsupp in the module map (#29 depends on #46 and #30).

## Suggested direction

Invert the dependency as Article/Leaflet did: define a Smartsupp-owned RAG-search contract (in `Smartsupp/Contracts/`, or reuse a shared contract under `Application.Shared.Rag`), implement and register the adapter on the KnowledgeBase side, and add a `Smartsupp → KnowledgeBase` rule to `ModuleBoundariesTests` to keep the boundary enforced.

<!-- harness-issue:tsk_b7c30a1020a14e79:3d6c3f02 -->
