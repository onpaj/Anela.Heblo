### task: define-smartsupp-knowledge-source-contract-and-adapter

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Smartsupp/Contracts/ISmartsuppKnowledgeSource.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/KnowledgeBaseSmartsuppKnowledgeSource.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs:44-47`
- Test: `backend/test/Anela.Heblo.Tests/KnowledgeBase/Infrastructure/KnowledgeBaseSmartsuppKnowledgeSourceTests.cs`

This task adds the new Smartsupp-owned contract, the KnowledgeBase-owned adapter that implements it, and the DI wiring — with no changes yet to `GenerateDraftReplyHandler` (that's the next task). It is structurally a copy of the existing `IArticleKnowledgeSource` / `KnowledgeBaseArticleKnowledgeSource` pair (see `backend/src/Anela.Heblo.Application/Features/Article/Contracts/IArticleKnowledgeSource.cs` and `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/KnowledgeBaseArticleKnowledgeSource.cs`), extended with a `DocumentId` field.

- [ ] **Step 1: Write the failing adapter test**

Create `backend/test/Anela.Heblo.Tests/KnowledgeBase/Infrastructure/KnowledgeBaseSmartsuppKnowledgeSourceTests.cs`:

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase.Infrastructure;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;
using Anela.Heblo.Application.Features.Smartsupp.Contracts;
using FluentAssertions;
using MediatR;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.Infrastructure;

public class KnowledgeBaseSmartsuppKnowledgeSourceTests
{
    private readonly Mock<IMediator> _mediator = new();

    private KnowledgeBaseSmartsuppKnowledgeSource CreateAdapter() =>
        new(_mediator.Object);

    [Fact]
    public async Task SearchAsync_DispatchesSearchDocumentsRequest_WithCorrectQueryAndTopK()
    {
        // Arrange
        var adapter = CreateAdapter();
        const string query = "test query";
        const int topK = 5;
        _mediator.Setup(m => m.Send(It.IsAny<SearchDocumentsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchDocumentsResponse { Chunks = [] });

        // Act
        await adapter.SearchAsync(query, topK, CancellationToken.None);

        // Assert
        _mediator.Verify(m => m.Send(
            It.Is<SearchDocumentsRequest>(r =>
                r.Query == query &&
                r.TopK == topK),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_MapsFiveFields_FromChunkResultToSmartsuppKnowledgeChunk()
    {
        // Arrange
        var adapter = CreateAdapter();
        var chunkId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        const string sourceFilename = "doc.pdf";
        const string content = "content text";
        const double score = 0.95;
        const string sourcePath = "/some/path";

        var chunkResult = new ChunkResult
        {
            ChunkId = chunkId,
            DocumentId = documentId,
            SourceFilename = sourceFilename,
            Content = content,
            Score = score,
            SourcePath = sourcePath,
        };

        _mediator.Setup(m => m.Send(It.IsAny<SearchDocumentsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchDocumentsResponse { Chunks = [chunkResult] });

        // Act
        var result = await adapter.SearchAsync("query", 3, CancellationToken.None);

        // Assert
        result.Should().HaveCount(1);
        result[0].ChunkId.Should().Be(chunkId);
        result[0].DocumentId.Should().Be(documentId);
        result[0].SourceFilename.Should().Be(sourceFilename);
        result[0].Content.Should().Be(content);
        result[0].Score.Should().Be(score);
    }

    [Fact]
    public async Task SearchAsync_ReturnsEmptyList_WhenNoChunks()
    {
        // Arrange
        var adapter = CreateAdapter();
        _mediator.Setup(m => m.Send(It.IsAny<SearchDocumentsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchDocumentsResponse { Chunks = [] });

        // Act
        var result = await adapter.SearchAsync("query", 5, CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
        result.Should().NotBeNull();
        result.Count.Should().Be(0);
    }

    [Fact]
    public async Task SearchAsync_PropagatesCancellationToken()
    {
        // Arrange
        var adapter = CreateAdapter();
        var cts = new CancellationTokenSource();
        _mediator.Setup(m => m.Send(It.IsAny<SearchDocumentsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SearchDocumentsResponse { Chunks = [] });

        // Act
        await adapter.SearchAsync("query", 5, cts.Token);

        // Assert
        _mediator.Verify(m => m.Send(
            It.IsAny<SearchDocumentsRequest>(),
            cts.Token), Times.Once);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~KnowledgeBaseSmartsuppKnowledgeSourceTests"`
Expected: Build FAILS — `KnowledgeBaseSmartsuppKnowledgeSource` and `Anela.Heblo.Application.Features.Smartsupp.Contracts` do not exist yet (CS0246).

- [ ] **Step 3: Create the `ISmartsuppKnowledgeSource` contract**

Create `backend/src/Anela.Heblo.Application/Features/Smartsupp/Contracts/ISmartsuppKnowledgeSource.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Smartsupp.Contracts;

/// <summary>
/// Smartsupp-owned read-only abstraction over the knowledge-base search index.
/// Implemented by the KnowledgeBase module via an adapter.
/// Structurally mirrors <c>IArticleKnowledgeSource</c> (string-query shape) — not
/// <c>ILeafletKnowledgeSource</c> (embedding-vector shape) — because
/// <c>GenerateDraftReplyHandler</c> builds a plain-text retrieval query and never
/// computes an embedding itself.
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

- [ ] **Step 4: Create the `KnowledgeBaseSmartsuppKnowledgeSource` adapter**

Create `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/KnowledgeBaseSmartsuppKnowledgeSource.cs`:

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;
using Anela.Heblo.Application.Features.Smartsupp.Contracts;
using MediatR;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Infrastructure;

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

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~KnowledgeBaseSmartsuppKnowledgeSourceTests"`
Expected: PASS (4 tests).

- [ ] **Step 6: Register the DI binding in `KnowledgeBaseModule`**

In `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs`, the current lines 44-47 read:

```csharp
        // Cross-module contract: KnowledgeBase implements Article's IArticleKnowledgeSource via adapter.
        // Scoped to match existing Article contract bindings above.
        services.AddScoped<IArticleKnowledgeSource, KnowledgeBaseArticleKnowledgeSource>();

```

Replace with (adding the new registration immediately after, and add the `using` for the Smartsupp contracts namespace at the top of the file alongside the existing `using Anela.Heblo.Application.Features.Article.Contracts;` on line 1):

```csharp
        // Cross-module contract: KnowledgeBase implements Article's IArticleKnowledgeSource via adapter.
        // Scoped to match existing Article contract bindings above.
        services.AddScoped<IArticleKnowledgeSource, KnowledgeBaseArticleKnowledgeSource>();

        // Cross-module contract: KnowledgeBase implements Smartsupp's ISmartsuppKnowledgeSource via adapter.
        // Same provider-owned-DI pattern as the Leaflet/Article bindings above.
        services.AddScoped<ISmartsuppKnowledgeSource, KnowledgeBaseSmartsuppKnowledgeSource>();

```

And change line 1 of the file from:

```csharp
using Anela.Heblo.Application.Features.Article.Contracts;
```

to:

```csharp
using Anela.Heblo.Application.Features.Article.Contracts;
using Anela.Heblo.Application.Features.Smartsupp.Contracts;
```

- [ ] **Step 7: Build to verify DI wiring compiles**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: Build succeeds, 0 errors.

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Smartsupp/Contracts/ISmartsuppKnowledgeSource.cs \
        backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/KnowledgeBaseSmartsuppKnowledgeSource.cs \
        backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs \
        backend/test/Anela.Heblo.Tests/KnowledgeBase/Infrastructure/KnowledgeBaseSmartsuppKnowledgeSourceTests.cs
git commit -m "feat: add ISmartsuppKnowledgeSource contract and KnowledgeBase adapter"
```

---
