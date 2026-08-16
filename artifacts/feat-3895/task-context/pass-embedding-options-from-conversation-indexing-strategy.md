### task: pass-embedding-options-from-conversation-indexing-strategy


Extends FR-4 to the fourth call site (`ConversationIndexingStrategy.CreateChunksAsync:30`), which the spec missed. Without this, the `KnowledgeBase:*` config rename in the last task would silently detach conversation indexing from KnowledgeBase's own embedding settings. This class has no options today, so it gains an `IOptions<KnowledgeBaseOptions>` constructor parameter — resolvable in DI already (`KnowledgeBaseModule.cs:32` registers `KnowledgeBaseDocIndexingStrategy`, which takes the same dependency, one line above `ConversationIndexingStrategy`).

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/ConversationIndexingStrategy.cs`
- Test: `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/ConversationIndexingStrategyTests.cs`

- [ ] **Step 1: Update the test fixture to construct the strategy with options, and write the failing test**

In `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/ConversationIndexingStrategyTests.cs`, replace the usings block (lines 1-6):

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using Anela.Heblo.Domain.Shared.Rag;
using Microsoft.Extensions.AI;
using Moq;
using Xunit;
```

with:

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase;
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using Anela.Heblo.Domain.Shared.Rag;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
```

Then replace the strategy construction at the end of the test-class constructor:

```csharp
        _strategy = new ConversationIndexingStrategy(
            _summarizer.Object,
            _embeddingGenerator.Object);
```

with:

```csharp
        _strategy = new ConversationIndexingStrategy(
            _summarizer.Object,
            _embeddingGenerator.Object,
            Options.Create(new KnowledgeBaseOptions()));
```

Then append this test inside the `ConversationIndexingStrategyTests` class, after `CreateChunksAsync_EmbeddingInputIsTopicSummary_NotFullText`:

```csharp
    [Fact]
    public async Task CreateChunksAsync_PassesKnowledgeBaseModelAndDimensionsToEmbeddingGenerator()
    {
        _summarizer
            .Setup(s => s.SummarizeTopicsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "Problém zákazníka: akné" });

        EmbeddingGenerationOptions? capturedOptions = null;
        _embeddingGenerator
            .Setup(e => e.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<string>, EmbeddingGenerationOptions?, CancellationToken>(
                (_, opts, _) => capturedOptions = opts)
            .ReturnsAsync(_generatedEmbeddings);

        var strategy = new ConversationIndexingStrategy(
            _summarizer.Object,
            _embeddingGenerator.Object,
            Options.Create(new KnowledgeBaseOptions
            {
                EmbeddingModel = "text-embedding-3-small",
                EmbeddingDimensions = 3072,
            }));

        await strategy.CreateChunksAsync("transcript", Guid.NewGuid(), CancellationToken.None);

        Assert.NotNull(capturedOptions);
        Assert.Equal("text-embedding-3-small", capturedOptions!.ModelId);
        Assert.Equal(3072, capturedOptions.Dimensions);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ConversationIndexingStrategyTests"
```

Expected: FAIL at build with `error CS1729: 'ConversationIndexingStrategy' does not contain a constructor that takes 3 arguments`.

- [ ] **Step 3: Inject the options and pass them through**

Replace the entire contents of `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/ConversationIndexingStrategy.cs` with:

```csharp
using Anela.Heblo.Domain.Features.KnowledgeBase;
using Anela.Heblo.Domain.Shared.Rag;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public class ConversationIndexingStrategy : IIndexingStrategy
{
    private readonly IConversationTopicSummarizer _summarizer;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
    private readonly KnowledgeBaseOptions _options;

    public ConversationIndexingStrategy(
        IConversationTopicSummarizer summarizer,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        IOptions<KnowledgeBaseOptions> options)
    {
        _summarizer = summarizer;
        _embeddingGenerator = embeddingGenerator;
        _options = options.Value;
    }

    public bool Supports(DocumentType documentType) =>
        documentType == DocumentType.Conversation;

    public async Task<IReadOnlyList<KnowledgeBaseChunk>> CreateChunksAsync(
        string cleanText, Guid documentId, CancellationToken ct)
    {
        var topics = await _summarizer.SummarizeTopicsAsync(cleanText, ct);
        if (topics.Count == 0)
            return [];

        var embeddings = await _embeddingGenerator.GenerateAsync(topics, _options.ToEmbeddingOptions(), ct);
        var chunks = new List<KnowledgeBaseChunk>(topics.Count);

        for (var i = 0; i < topics.Count; i++)
        {
            chunks.Add(new KnowledgeBaseChunk
            {
                Id = Guid.NewGuid(),
                DocumentId = documentId,
                ChunkIndex = i,
                Content = cleanText,
                Summary = topics[i],
                DocumentType = DocumentType.Conversation,
                Embedding = embeddings[i].Vector.ToArray(),
            });
        }

        return chunks;
    }
}
```

Note: `ConversationIndexingStrategy` lives in namespace `Anela.Heblo.Application.Features.KnowledgeBase.Services`, so `KnowledgeBaseOptions` (namespace `Anela.Heblo.Application.Features.KnowledgeBase`) resolves without an extra `using`, and `ToEmbeddingOptions()` is inherited from `RagFeatureOptions`.

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ConversationIndexingStrategyTests"
```

Expected: PASS — 7 tests.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/ConversationIndexingStrategy.cs \
        backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/ConversationIndexingStrategyTests.cs
git commit -m "fix(knowledgebase): pass feature embedding model/dimensions when indexing conversations"
```

---
