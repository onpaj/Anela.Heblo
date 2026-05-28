## Module
Article

## Finding
`GatherContextStep` has a direct compile-time dependency on `SearchDocumentsRequest`, a type that lives inside the KnowledgeBase module's UseCase folder:

```csharp
// backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/GatherContextStep.cs:2
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;
```

The request object is then instantiated and dispatched via MediatR (lines 88–90):

```csharp
var response = await _mediator.Send(
    new SearchDocumentsRequest { Query = query, TopK = _options.KnowledgeBaseTopK },
    ct);
```

`SearchDocumentsRequest` is declared at:
`backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/SearchDocuments/SearchDocumentsRequest.cs`

This is a type owned and controlled by the KnowledgeBase module's internal implementation, not its `Contracts/` folder. The same pattern exists in `Smartsupp/UseCases/GenerateDraftReply/GenerateDraftReplyHandler.cs:1`, so this is a recurring cross-module coupling.

The inconsistency is especially visible within the Article module itself: access to the style guide already follows the correct pattern — `IArticleStyleGuideSource` is defined in `Article/Contracts/` and implemented by the KnowledgeBase module via an adapter — but the KB search path skips the contract entirely.

## Why it matters
- **Hard compile-time coupling**: Renaming or restructuring `SearchDocumentsRequest` inside KnowledgeBase breaks the Article (and Smartsupp) modules at compile time.
- **Bypasses module contract discipline**: `development_guidelines.md` is explicit: "Communication between modules exclusively through `contracts/`." Using a UseCase-internal type as a cross-module API violates this, regardless of whether MediatR is the dispatch mechanism.
- **Not caught by `ModuleBoundariesTests`**: The existing architecture test enforces an `Article → KnowledgeBase` namespace allowlist, but if `SearchDocumentsRequest` were ever moved or split, the test might not catch a broken caller.
- **Sets a precedent**: Two modules (Article, Smartsupp) already use this shortcut. Without a contract boundary, more callers will follow the same path.

## Suggested fix

1. **Article defines a consumer-owned contract** in its own `Contracts/` folder:

```csharp
// backend/src/Anela.Heblo.Application/Features/Article/Contracts/IArticleKnowledgeSource.cs
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

2. **KnowledgeBase provides an adapter** (in `KnowledgeBase/Infrastructure/`):

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

3. **KnowledgeBase registers the binding** in `KnowledgeBaseModule.cs`:
   `services.AddScoped<IArticleKnowledgeSource, KnowledgeBaseArticleSourceAdapter>();`

4. **`GatherContextStep` injects `IArticleKnowledgeSource`** instead of `IMediator` for the KB branch:

```csharp
// GatherContextStep.cs — remove the IMediator dependency for the KB path
private readonly IArticleKnowledgeSource _knowledgeSource;

// In GatherKnowledgeBaseSnippetsAsync:
var chunks = await _knowledgeSource.SearchAsync(query, _options.KnowledgeBaseTopK, ct);
snippets.AddRange(chunks.Select(c => new ContextSnippet { ... }));
```

5. Apply the same contract pattern in Smartsupp as a follow-up.

---
_Filed by daily arch-review routine on 2026-05-27._