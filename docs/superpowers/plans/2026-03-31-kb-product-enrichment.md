# KB Product Mention Enrichment — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace product name mentions in KB answers with inline enriched references (canonical name + product code + URL) by injecting a product table into the LLM prompt and post-processing the response in `PostAnswerEnrichmentMiddleware`.

**Architecture:** `AskQuestionHandler` loads a TTL-cached product lookup (Product+Goods types only) and injects it as a compact table into the system prompt with annotation instructions. The LLM replaces product names with `(CODE)` tokens. `PostAnswerEnrichmentMiddleware` (DelegatingChatClient) intercepts the response and regex-replaces each `(CODE)` token with `[Name (CODE)](url)` using the same cached lookup.

**Tech Stack:** .NET 8, MediatR, Microsoft.Extensions.AI (`DelegatingChatClient`), Moq + xUnit, `IServiceScopeFactory` for scoped → singleton catalog access.

---

## File Map

| Action | Path |
|--------|------|
| Create | `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Pipeline/ProductEnrichmentEntry.cs` |
| Create | `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Pipeline/IProductEnrichmentCache.cs` |
| Create | `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Pipeline/ProductEnrichmentCache.cs` |
| Modify | `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Pipeline/PostAnswerEnrichmentMiddleware.cs` |
| Modify | `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseOptions.cs` |
| Modify | `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/AskQuestion/AskQuestionHandler.cs` |
| Modify | `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs` |
| Modify | `backend/src/Adapters/Anela.Heblo.Adapters.Anthropic/AnthropicAdapterServiceCollectionExtensions.cs` |
| Create | `backend/test/Anela.Heblo.Tests/KnowledgeBase/Pipeline/ProductEnrichmentCacheTests.cs` |
| Create | `backend/test/Anela.Heblo.Tests/KnowledgeBase/Pipeline/PostAnswerEnrichmentMiddlewareTests.cs` |
| Modify | `backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/AskQuestionHandlerTests.cs` |

---

## Task 1: `ProductEnrichmentEntry` and `IProductEnrichmentCache`

These are pure data types — no logic, no tests needed.

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Pipeline/ProductEnrichmentEntry.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Pipeline/IProductEnrichmentCache.cs`

- [ ] **Step 1: Create `ProductEnrichmentEntry`**

```csharp
namespace Anela.Heblo.Application.Features.KnowledgeBase.Pipeline;

public class ProductEnrichmentEntry
{
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string? Url { get; set; } // placeholder — populated in a future story
}
```

- [ ] **Step 2: Create `IProductEnrichmentCache`**

```csharp
namespace Anela.Heblo.Application.Features.KnowledgeBase.Pipeline;

public interface IProductEnrichmentCache
{
    Task<IReadOnlyDictionary<string, ProductEnrichmentEntry>> GetProductLookupAsync(
        CancellationToken ct = default);
}
```

- [ ] **Step 3: Verify the project builds**

```bash
cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: build succeeds with 0 errors.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Pipeline/ProductEnrichmentEntry.cs \
        backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Pipeline/IProductEnrichmentCache.cs
git commit -m "feat(kb): add ProductEnrichmentEntry and IProductEnrichmentCache types"
```

---

## Task 2: `ProductEnrichmentCache` implementation (TDD)

Singleton cache backed by `ICatalogRepository`. Uses `IServiceScopeFactory` to resolve the scoped repository. TTL configured via `KnowledgeBaseOptions.ProductEnrichmentCacheTtlMinutes`.

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Pipeline/ProductEnrichmentCache.cs`
- Create: `backend/test/Anela.Heblo.Tests/KnowledgeBase/Pipeline/ProductEnrichmentCacheTests.cs`

- [ ] **Step 1: Write failing tests**

Create `backend/test/Anela.Heblo.Tests/KnowledgeBase/Pipeline/ProductEnrichmentCacheTests.cs`:

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase;
using Anela.Heblo.Application.Features.KnowledgeBase.Pipeline;
using Anela.Heblo.Domain.Features.Catalog;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.Pipeline;

public class ProductEnrichmentCacheTests
{
    private readonly Mock<IServiceScopeFactory> _scopeFactory = new();
    private readonly Mock<IServiceScope> _scope = new();
    private readonly Mock<IServiceProvider> _serviceProvider = new();
    private readonly Mock<ICatalogRepository> _repository = new();

    public ProductEnrichmentCacheTests()
    {
        _scopeFactory.Setup(f => f.CreateScope()).Returns(_scope.Object);
        _scope.Setup(s => s.ServiceProvider).Returns(_serviceProvider.Object);
        _serviceProvider
            .Setup(sp => sp.GetService(typeof(ICatalogRepository)))
            .Returns(_repository.Object);
    }

    private ProductEnrichmentCache Create(int ttlMinutes = 60) =>
        new(_scopeFactory.Object, Options.Create(new KnowledgeBaseOptions
        {
            ProductEnrichmentCacheTtlMinutes = ttlMinutes
        }));

    private void SetupRepository(params CatalogAggregate[] products)
    {
        _repository
            .Setup(r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(products);
    }

    [Fact]
    public async Task GetProductLookupAsync_ReturnsOnlyProductAndGoodsTypes()
    {
        var product = new CatalogAggregate { ProductCode = "PRD001", ProductName = "Sérum ABC", Type = ProductType.Product };
        var goods = new CatalogAggregate { ProductCode = "GDS001", ProductName = "Krém XYZ", Type = ProductType.Goods };
        // FindAsync is called with a filter — we simulate it already filtered
        SetupRepository(product, goods);

        var cache = Create();
        var lookup = await cache.GetProductLookupAsync();

        Assert.Equal(2, lookup.Count);
        Assert.True(lookup.ContainsKey("PRD001"));
        Assert.True(lookup.ContainsKey("GDS001"));
        Assert.Equal("Sérum ABC", lookup["PRD001"].ProductName);
        Assert.Null(lookup["PRD001"].Url);
    }

    [Fact]
    public async Task GetProductLookupAsync_WithinTtl_ReturnsCachedResult_RepositoryCalledOnce()
    {
        SetupRepository(new CatalogAggregate { ProductCode = "PRD001", ProductName = "Sérum ABC", Type = ProductType.Product });
        var cache = Create(ttlMinutes: 60);

        await cache.GetProductLookupAsync();
        await cache.GetProductLookupAsync();

        _repository.Verify(
            r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetProductLookupAsync_AfterTtlExpiry_RefreshesFromRepository()
    {
        SetupRepository(new CatalogAggregate { ProductCode = "PRD001", ProductName = "Sérum ABC", Type = ProductType.Product });
        var cache = Create(ttlMinutes: 0); // TTL = 0 → always expired

        await cache.GetProductLookupAsync();
        await cache.GetProductLookupAsync();

        _repository.Verify(
            r => r.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }
}
```

- [ ] **Step 2: Run tests — expect compilation failure (class not yet created)**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ProductEnrichmentCacheTests" 2>&1 | head -30
```

Expected: build error — `ProductEnrichmentCache` does not exist.

- [ ] **Step 3: Implement `ProductEnrichmentCache`**

Create `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Pipeline/ProductEnrichmentCache.cs`:

```csharp
using Anela.Heblo.Domain.Features.Catalog;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Pipeline;

public class ProductEnrichmentCache : IProductEnrichmentCache
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<KnowledgeBaseOptions> _options;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private IReadOnlyDictionary<string, ProductEnrichmentEntry> _cache =
        new Dictionary<string, ProductEnrichmentEntry>();
    private DateTime _lastLoaded = DateTime.MinValue;

    public ProductEnrichmentCache(
        IServiceScopeFactory scopeFactory,
        IOptions<KnowledgeBaseOptions> options)
    {
        _scopeFactory = scopeFactory;
        _options = options;
    }

    public async Task<IReadOnlyDictionary<string, ProductEnrichmentEntry>> GetProductLookupAsync(
        CancellationToken ct = default)
    {
        var ttl = TimeSpan.FromMinutes(_options.Value.ProductEnrichmentCacheTtlMinutes);

        if (DateTime.UtcNow - _lastLoaded < ttl)
            return _cache;

        await _lock.WaitAsync(ct);
        try
        {
            if (DateTime.UtcNow - _lastLoaded < ttl)
                return _cache;

            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<ICatalogRepository>();

            var products = await repository.FindAsync(
                p => p.Type == ProductType.Product || p.Type == ProductType.Goods,
                ct);

            _cache = products.ToDictionary(
                p => p.ProductCode,
                p => new ProductEnrichmentEntry
                {
                    ProductCode = p.ProductCode,
                    ProductName = p.ProductName,
                    Url = null
                });

            _lastLoaded = DateTime.UtcNow;
            return _cache;
        }
        finally
        {
            _lock.Release();
        }
    }
}
```

- [ ] **Step 4: Run tests — expect pass**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ProductEnrichmentCacheTests" -v normal
```

Expected: 3 tests pass.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Pipeline/ProductEnrichmentCache.cs \
        backend/test/Anela.Heblo.Tests/KnowledgeBase/Pipeline/ProductEnrichmentCacheTests.cs
git commit -m "feat(kb): implement ProductEnrichmentCache with TTL and IServiceScopeFactory"
```

---

## Task 3: `PostAnswerEnrichmentMiddleware` enrichment logic (TDD)

The middleware intercepts the LLM response, finds `(CODE)` tokens present in the catalog dict, and replaces them with `[Name (CODE)](url)` (or `Name (CODE)` when URL is null).

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Pipeline/PostAnswerEnrichmentMiddleware.cs`
- Create: `backend/test/Anela.Heblo.Tests/KnowledgeBase/Pipeline/PostAnswerEnrichmentMiddlewareTests.cs`

- [ ] **Step 1: Write failing tests**

Create `backend/test/Anela.Heblo.Tests/KnowledgeBase/Pipeline/PostAnswerEnrichmentMiddlewareTests.cs`:

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase.Pipeline;
using Microsoft.Extensions.AI;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.Pipeline;

public class PostAnswerEnrichmentMiddlewareTests
{
    private readonly Mock<IChatClient> _inner = new();
    private readonly Mock<IProductEnrichmentCache> _cache = new();

    private PostAnswerEnrichmentMiddleware Create() =>
        new(_inner.Object, _cache.Object);

    private void SetupInner(string responseText)
    {
        _inner
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, responseText)]));
    }

    private void SetupCache(params (string code, string name, string? url)[] entries)
    {
        var dict = entries.ToDictionary(
            e => e.code,
            e => new ProductEnrichmentEntry { ProductCode = e.code, ProductName = e.name, Url = e.url });
        _cache
            .Setup(c => c.GetProductLookupAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(dict);
    }

    [Fact]
    public async Task GetResponseAsync_CodeWithUrl_ReplacesWithMarkdownLink()
    {
        SetupInner("Doporučujeme aplikovat (AKL001) na čistou pleť.");
        SetupCache(("AKL001", "Sérum ABC", "https://anela.cz/produkty/serum-abc"));

        var result = await Create().GetResponseAsync([], null, default);

        Assert.Equal(
            "Doporučujeme aplikovat [Sérum ABC (AKL001)](https://anela.cz/produkty/serum-abc) na čistou pleť.",
            result.Text);
    }

    [Fact]
    public async Task GetResponseAsync_CodeWithoutUrl_ReplacesWithPlainText()
    {
        SetupInner("Použijte (KRM002) ráno i večer.");
        SetupCache(("KRM002", "Hydratační krém", null));

        var result = await Create().GetResponseAsync([], null, default);

        Assert.Equal("Použijte Hydratační krém (KRM002) ráno i večer.", result.Text);
    }

    [Fact]
    public async Task GetResponseAsync_CodeNotInCatalog_LeavesTokenUnchanged()
    {
        SetupInner("Odpověď obsahuje (UNKNOWN) token.");
        SetupCache(("AKL001", "Sérum ABC", null));

        var result = await Create().GetResponseAsync([], null, default);

        Assert.Equal("Odpověď obsahuje (UNKNOWN) token.", result.Text);
    }

    [Fact]
    public async Task GetResponseAsync_MultipleCodesInAnswer_AllReplaced()
    {
        SetupInner("Použijte (AKL001) a poté (KRM002).");
        SetupCache(
            ("AKL001", "Sérum ABC", "https://anela.cz/serum"),
            ("KRM002", "Hydratační krém", "https://anela.cz/krem"));

        var result = await Create().GetResponseAsync([], null, default);

        Assert.Equal(
            "Použijte [Sérum ABC (AKL001)](https://anela.cz/serum) a poté [Hydratační krém (KRM002)](https://anela.cz/krem).",
            result.Text);
    }

    [Fact]
    public async Task GetResponseAsync_NoCodesInAnswer_TextUnchanged()
    {
        const string text = "Tato odpověď neobsahuje žádný kód produktu.";
        SetupInner(text);
        SetupCache(("AKL001", "Sérum ABC", null));

        var result = await Create().GetResponseAsync([], null, default);

        Assert.Equal(text, result.Text);
    }
}
```

- [ ] **Step 2: Run tests — expect compilation failure**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~PostAnswerEnrichmentMiddlewareTests" 2>&1 | head -30
```

Expected: build error — constructor signature mismatch.

- [ ] **Step 3: Implement the enrichment logic in `PostAnswerEnrichmentMiddleware`**

Replace the full content of `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Pipeline/PostAnswerEnrichmentMiddleware.cs`:

```csharp
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Pipeline;

public class PostAnswerEnrichmentMiddleware : DelegatingChatClient
{
    private static readonly Regex ProductCodePattern = new(@"\(([A-Z0-9]+)\)", RegexOptions.Compiled);
    private readonly IProductEnrichmentCache _cache;

    public PostAnswerEnrichmentMiddleware(IChatClient inner, IProductEnrichmentCache cache)
        : base(inner)
    {
        _cache = cache;
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(chatMessages, options, cancellationToken);
        var rawText = response.Text ?? string.Empty;

        if (string.IsNullOrEmpty(rawText))
            return response;

        var lookup = await _cache.GetProductLookupAsync(cancellationToken);
        var enriched = ProductCodePattern.Replace(rawText, match =>
        {
            var code = match.Groups[1].Value;
            if (!lookup.TryGetValue(code, out var entry))
                return match.Value;

            return string.IsNullOrEmpty(entry.Url)
                ? $"{entry.ProductName} ({code})"
                : $"[{entry.ProductName} ({code})]({entry.Url})";
        });

        if (enriched == rawText)
            return response;

        return new ChatResponse([new ChatMessage(ChatRole.Assistant, enriched)]);
    }
}
```

- [ ] **Step 4: Run tests — expect pass**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~PostAnswerEnrichmentMiddlewareTests" -v normal
```

Expected: 5 tests pass.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Pipeline/PostAnswerEnrichmentMiddleware.cs \
        backend/test/Anela.Heblo.Tests/KnowledgeBase/Pipeline/PostAnswerEnrichmentMiddlewareTests.cs
git commit -m "feat(kb): implement product code enrichment in PostAnswerEnrichmentMiddleware"
```

---

## Task 4: Update `AskQuestionHandler` — product table injection (TDD)

The handler loads the product lookup and injects a compact `CODE | Název` table into the system prompt via a new `{products}` placeholder.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/AskQuestion/AskQuestionHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/AskQuestionHandlerTests.cs`

- [ ] **Step 1: Extend `AskQuestionHandlerTests` with product table tests**

Replace the full content of `backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/AskQuestionHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase;
using Anela.Heblo.Application.Features.KnowledgeBase.Pipeline;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.AskQuestion;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;
using MediatR;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.UseCases;

public class AskQuestionHandlerTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<IChatClient> _chatClient = new();
    private readonly Mock<IProductEnrichmentCache> _enrichmentCache = new();

    private AskQuestionHandler CreateHandler(KnowledgeBaseOptions? options = null) =>
        new(_mediator.Object, _chatClient.Object, Options.Create(options ?? new KnowledgeBaseOptions()),
            _enrichmentCache.Object);

    private void SetupEmptyCache() =>
        _enrichmentCache
            .Setup(c => c.GetProductLookupAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, ProductEnrichmentEntry>());

    [Fact]
    public async Task Handle_ReturnsAnswerWithSources()
    {
        SetupEmptyCache();
        var searchResponse = new SearchDocumentsResponse
        {
            Chunks =
            [
                new ChunkResult
                {
                    ChunkId = Guid.NewGuid(),
                    DocumentId = Guid.NewGuid(),
                    Content = "Max phenoxyethanol 1.0% per EU regulation",
                    Score = 0.95,
                    SourceFilename = "EU_reg.pdf",
                    SourcePath = "/archived/EU_reg.pdf"
                }
            ]
        };

        _mediator
            .Setup(m => m.Send(It.IsAny<SearchDocumentsRequest>(), default))
            .ReturnsAsync(searchResponse);

        var chatResponse = new ChatResponse([new ChatMessage(ChatRole.Assistant, "The maximum allowed concentration is 1.0%.")]);
        _chatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                default))
            .ReturnsAsync(chatResponse);

        var result = await CreateHandler().Handle(
            new AskQuestionRequest { Question = "Max phenoxyethanol?", TopK = 5 },
            default);

        Assert.Equal("The maximum allowed concentration is 1.0%.", result.Answer);
        Assert.Single(result.Sources);
        Assert.Equal("EU_reg.pdf", result.Sources[0].Filename);
    }

    [Fact]
    public async Task Handle_EmptyChunks_ReturnsFallbackAnswerWithNoSources()
    {
        SetupEmptyCache();
        _mediator
            .Setup(m => m.Send(It.IsAny<SearchDocumentsRequest>(), default))
            .ReturnsAsync(new SearchDocumentsResponse { Chunks = [], BelowThresholdCount = 3 });

        var result = await CreateHandler().Handle(
            new AskQuestionRequest { Question = "Co mi poradis na akne?", TopK = 5 },
            default);

        Assert.Equal("V dostupných dokumentech jsem nenašla relevantní informaci k vaší otázce.", result.Answer);
        Assert.Empty(result.Sources);
        _chatClient.Verify(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            default), Times.Never);
    }

    [Fact]
    public async Task Handle_WithProducts_InjectsProductTableIntoSystemPrompt()
    {
        var products = new Dictionary<string, ProductEnrichmentEntry>
        {
            ["AKL001"] = new() { ProductCode = "AKL001", ProductName = "Sérum ABC" },
            ["KRM002"] = new() { ProductCode = "KRM002", ProductName = "Hydratační krém" }
        };
        _enrichmentCache
            .Setup(c => c.GetProductLookupAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(products);

        _mediator
            .Setup(m => m.Send(It.IsAny<SearchDocumentsRequest>(), default))
            .ReturnsAsync(new SearchDocumentsResponse
            {
                Chunks = [new ChunkResult { ChunkId = Guid.NewGuid(), DocumentId = Guid.NewGuid(), Content = "some content", Score = 0.9, SourceFilename = "doc.pdf", SourcePath = "/doc.pdf" }]
            });

        IEnumerable<ChatMessage>? capturedMessages = null;
        _chatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                default))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>(
                (msgs, _, _) => capturedMessages = msgs)
            .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, "odpověď")]));

        var options = new KnowledgeBaseOptions
        {
            AskQuestionSystemPrompt = "Kontext: {context}\nProdukty: {products}\nDotaz: {query}"
        };

        await CreateHandler(options).Handle(
            new AskQuestionRequest { Question = "Co doporučuješ?", TopK = 5 },
            default);

        var systemMessage = capturedMessages!.First(m => m.Role == ChatRole.System).Text!;
        Assert.Contains("AKL001 | Sérum ABC", systemMessage);
        Assert.Contains("KRM002 | Hydratační krém", systemMessage);
    }

    [Fact]
    public async Task Handle_EmptyProductCatalog_ProductsPlaceholderReplacedWithEmptyString()
    {
        SetupEmptyCache();
        _mediator
            .Setup(m => m.Send(It.IsAny<SearchDocumentsRequest>(), default))
            .ReturnsAsync(new SearchDocumentsResponse
            {
                Chunks = [new ChunkResult { ChunkId = Guid.NewGuid(), DocumentId = Guid.NewGuid(), Content = "some content", Score = 0.9, SourceFilename = "doc.pdf", SourcePath = "/doc.pdf" }]
            });

        IEnumerable<ChatMessage>? capturedMessages = null;
        _chatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                default))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>(
                (msgs, _, _) => capturedMessages = msgs)
            .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, "odpověď")]));

        var options = new KnowledgeBaseOptions
        {
            AskQuestionSystemPrompt = "Kontext: {context}\nProdukty: {products}\nDotaz: {query}"
        };

        await CreateHandler(options).Handle(
            new AskQuestionRequest { Question = "Dotaz?", TopK = 5 },
            default);

        var systemMessage = capturedMessages!.First(m => m.Role == ChatRole.System).Text!;
        Assert.DoesNotContain("{products}", systemMessage);
        Assert.DoesNotContain("AKL001", systemMessage);
    }
}
```

- [ ] **Step 2: Run tests — expect compilation failure (handler constructor mismatch)**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~AskQuestionHandlerTests" 2>&1 | head -30
```

Expected: build error.

- [ ] **Step 3: Update `AskQuestionHandler`**

Replace the full content of `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/AskQuestion/AskQuestionHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase.Pipeline;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;
using MediatR;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.AskQuestion;

public class AskQuestionHandler : IRequestHandler<AskQuestionRequest, AskQuestionResponse>
{
    private readonly IMediator _mediator;
    private readonly IChatClient _chatClient;
    private readonly KnowledgeBaseOptions _options;
    private readonly IProductEnrichmentCache _enrichmentCache;

    public AskQuestionHandler(
        IMediator mediator,
        IChatClient chatClient,
        IOptions<KnowledgeBaseOptions> options,
        IProductEnrichmentCache enrichmentCache)
    {
        _mediator = mediator;
        _chatClient = chatClient;
        _options = options.Value;
        _enrichmentCache = enrichmentCache;
    }

    public async Task<AskQuestionResponse> Handle(
        AskQuestionRequest request,
        CancellationToken cancellationToken)
    {
        var searchResult = await _mediator.Send(
            new SearchDocumentsRequest { Query = request.Question, TopK = request.TopK },
            cancellationToken);

        if (!searchResult.Chunks.Any())
        {
            return new AskQuestionResponse
            {
                Answer = "V dostupných dokumentech jsem nenašla relevantní informaci k vaší otázce.",
                Sources = []
            };
        }

        var context = string.Join("\n\n---\n\n", searchResult.Chunks.Select(c => c.Content));

        var productLookup = await _enrichmentCache.GetProductLookupAsync(cancellationToken);
        var productTable = productLookup.Any()
            ? string.Join("\n", productLookup.Values.Select(p => $"{p.ProductCode} | {p.ProductName}"))
            : string.Empty;

        var systemPrompt = _options.AskQuestionSystemPrompt
            .Replace("{context}", context)
            .Replace("{products}", productTable)
            .Replace("{query}", request.Question);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, request.Question)
        };

        var response = await _chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);
        var answer = response.Text ?? string.Empty;

        return new AskQuestionResponse
        {
            Answer = answer,
            Sources = searchResult.Chunks.Select(c => new SourceReference
            {
                ChunkId = c.ChunkId,
                DocumentId = c.DocumentId,
                Filename = c.SourceFilename,
                Excerpt = c.Content[..Math.Min(200, c.Content.Length)],
                Score = c.Score
            }).ToList()
        };
    }
}
```

- [ ] **Step 4: Run handler tests — expect pass**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~AskQuestionHandlerTests" -v normal
```

Expected: 4 tests pass.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/AskQuestion/AskQuestionHandler.cs \
        backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/AskQuestionHandlerTests.cs
git commit -m "feat(kb): inject product table into AskQuestion system prompt"
```

---

## Task 5: `KnowledgeBaseOptions` update + registration wiring

Update the system prompt default to include the `{products}` section and annotation instruction. Register the singleton cache and update the middleware factory.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseOptions.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Anthropic/AnthropicAdapterServiceCollectionExtensions.cs`

- [ ] **Step 1: Add `ProductEnrichmentCacheTtlMinutes` to `KnowledgeBaseOptions` and update the system prompt default**

In `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseOptions.cs`, add the new property after `ChatMaxTokens`:

```csharp
/// <summary>
/// How long the product enrichment lookup dictionary is cached in memory (in minutes).
/// Default: 60. Set to 0 to disable caching (always reload).
/// </summary>
public int ProductEnrichmentCacheTtlMinutes { get; set; } = 60;
```

Replace the `AskQuestionSystemPrompt` default value (lines 141–161 in the current file) with:

```csharp
/// <summary>
/// System prompt used by AskQuestionHandler. Supports {context}, {products} and {query} placeholders.
/// {context} is replaced with retrieved chunks; {products} with the product table; {query} with the user's question.
/// </summary>
public string AskQuestionSystemPrompt { get; set; } =
    """
    Jsi odborná poradkyně kosmetické firmy Anela. Odpovídáš zákazníkům
    na dotazy o péči o pleť a produktech Anela.

    Odpovídej výhradně na základě poskytnutého kontextu z předchozích
    konverzací. Pokud kontext neobsahuje relevantní informaci, řekni to
    přímo – nevymýšlej doporučení.

    Při odpovědi:
    - Doporučuj konkrétní produkty Anela, pokud jsou v kontextu zmíněny
    - Zohledni typ pleti a potíže zákazníka
    - Odpovídej v češtině, přátelsky ale odborně
    - Pokud kontext obsahuje více podobných případů, syntetizuj je
    - Pokud zmiňuješ produkt Anela, nahraď celý název produktu jeho kódem
      v závorce (např. (AKL001)). Použij pouze kódy z přiloženého seznamu produktů.

    Kontext z podobných konverzací:
    {context}

    Produkty Anela (CODE | Název):
    {products}

    Dotaz zákazníka:
    {query}
    """;
```

- [ ] **Step 2: Register `IProductEnrichmentCache` in `KnowledgeBaseModule`**

In `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs`, add the singleton registration after the existing `AddScoped` lines (the `Pipeline` using is already present at the top of the file):

```csharp
services.AddSingleton<IProductEnrichmentCache, ProductEnrichmentCache>();
```

- [ ] **Step 3: Update `AnthropicAdapterServiceCollectionExtensions` to pass the cache to the middleware**

Replace the `.Use(...)` line in `backend/src/Adapters/Anela.Heblo.Adapters.Anthropic/AnthropicAdapterServiceCollectionExtensions.cs`:

```csharp
.Use(inner => new PostAnswerEnrichmentMiddleware(
    inner,
    sp.GetRequiredService<IProductEnrichmentCache>()));
```

The full updated method:

```csharp
public static IServiceCollection AddAnthropicAdapter(this IServiceCollection services, IConfiguration configuration)
{
    services.Configure<AnthropicOptions>(opts =>
    {
        opts.ApiKey = configuration["Anthropic:ApiKey"] ?? "";
        opts.Model = configuration["KnowledgeBase:ChatModel"] ?? opts.Model;
        opts.MaxTokens = configuration.GetValue("KnowledgeBase:ChatMaxTokens", opts.MaxTokens);
    });
    services.AddHttpClient("Anthropic");

    services.AddChatClient(sp =>
        new AnthropicChatClient(
            sp.GetRequiredService<IOptions<AnthropicOptions>>(),
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<AnthropicChatClient>>()))
        .UseLogging()
        .Use(inner => new PostAnswerEnrichmentMiddleware(
            inner,
            sp.GetRequiredService<IProductEnrichmentCache>()));

    return services;
}
```

Add the using at the top:
```csharp
using Anela.Heblo.Application.Features.KnowledgeBase.Pipeline;
```

- [ ] **Step 4: Build the full solution**

```bash
cd backend && dotnet build Anela.Heblo.sln
```

Expected: 0 errors.

- [ ] **Step 5: Run the full test suite**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj -v normal
```

Expected: all tests pass (including the existing `AskQuestionHandlerTests`, new pipeline tests).

- [ ] **Step 6: Run `dotnet format`**

```bash
cd backend && dotnet format Anela.Heblo.sln
```

Expected: no changes (if changes are made, stage and include in the commit below).

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseOptions.cs \
        backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs \
        backend/src/Adapters/Anela.Heblo.Adapters.Anthropic/AnthropicAdapterServiceCollectionExtensions.cs
git commit -m "feat(kb): wire product enrichment cache and update system prompt with {products} placeholder"
```
