# Knowledge Base Code Review Fixes Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix 8 code quality issues identified in PR #383 code review — eliminating DRY violations, pointless async noise, leaky abstractions, magic strings, and improving type safety.

**Architecture:** All fixes are isolated to the Knowledge Base feature and its two adapter projects. No DB migrations are required. Each fix is independent and can be committed separately.

**Tech Stack:** .NET 8, C#, MediatR, EF Core 8, Polly 8, ASP.NET Core authorization

---

## Chunk 1: Quick wins — naming, formatting, magic strings (Issues 2, 5, 6, 8)

### Task 1: Fix pointless `await Task.CompletedTask` in `KnowledgeBaseRepository` (Issue 2)

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/KnowledgeBase/KnowledgeBaseRepository.cs:17-21`

**Background:** `AddDocumentAsync` calls `_context.KnowledgeBaseDocuments.Add(document)` (synchronous) then does `await Task.CompletedTask` which allocates a state machine for nothing. The interface requires `Task` return, so we just return `Task.CompletedTask` directly without making the method `async`.

- [ ] **Step 1: Edit `KnowledgeBaseRepository.cs` lines 17–21**

Replace:
```csharp
public async Task AddDocumentAsync(KnowledgeBaseDocument document, CancellationToken ct = default)
{
    _context.KnowledgeBaseDocuments.Add(document);
    await Task.CompletedTask;
}
```
With:
```csharp
public Task AddDocumentAsync(KnowledgeBaseDocument document, CancellationToken ct = default)
{
    _context.KnowledgeBaseDocuments.Add(document);
    return Task.CompletedTask;
}
```

- [ ] **Step 2: Build to verify no compilation errors**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet build src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj --no-restore -q
```
Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
git add backend/src/Anela.Heblo.Persistence/KnowledgeBase/KnowledgeBaseRepository.cs
git commit -m "fix: remove pointless async/await in AddDocumentAsync"
```

---

### Task 2: Rename `_claude` field to `_answerService` in `AskQuestionHandler` (Issue 5)

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/AskQuestion/AskQuestionHandler.cs`

**Background:** The field `_claude` is named after the concrete provider (Anthropic Claude), not the abstraction (`IAnswerService`). If the adapter is switched to a different LLM, the name is misleading. Rename to `_answerService`.

- [ ] **Step 1: Edit `AskQuestionHandler.cs` — rename field declaration and usages**

File: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/AskQuestion/AskQuestionHandler.cs`

Replace line 10:
```csharp
    private readonly IAnswerService _claude;
```
With:
```csharp
    private readonly IAnswerService _answerService;
```

Replace line 12:
```csharp
    public AskQuestionHandler(IMediator mediator, IAnswerService claude)
    {
        _mediator = mediator;
        _claude = claude;
    }
```
With:
```csharp
    public AskQuestionHandler(IMediator mediator, IAnswerService answerService)
    {
        _mediator = mediator;
        _answerService = answerService;
    }
```

Replace line 28:
```csharp
        var answer = await _claude.GenerateAnswerAsync(
```
With:
```csharp
        var answer = await _answerService.GenerateAnswerAsync(
```

- [ ] **Step 2: Build**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj --no-restore -q
```
Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/AskQuestion/AskQuestionHandler.cs
git commit -m "fix: rename _claude to _answerService to avoid leaking concrete provider name"
```

---

### Task 3: Extract policy name constant and eliminate magic string duplication (Issue 6)

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Authorization/AuthorizationConstants.cs`
- Modify: `backend/src/Anela.Heblo.API/Extensions/AuthenticationExtensions.cs:109`
- Modify: `backend/src/Anela.Heblo.API/Controllers/KnowledgeBaseController.cs:50,58`

**Background:** The string `"KnowledgeBaseUpload"` is used in 3 places: policy registration (`AuthenticationExtensions.cs`) and two `[Authorize(Policy = "...")]` attributes on the controller. If the string changes, all 3 must change together. Extract to a constant in `AuthorizationConstants`.

- [ ] **Step 1: Add `Policies` nested class to `AuthorizationConstants.cs`**

After the `Roles` class closing brace, add:

```csharp
    /// <summary>
    /// Authorization policy names
    /// </summary>
    public static class Policies
    {
        /// <summary>
        /// Policy required for uploading and deleting Knowledge Base documents
        /// </summary>
        public const string KnowledgeBaseUpload = "KnowledgeBaseUpload";
    }
```

- [ ] **Step 2: Update `AuthenticationExtensions.cs` line 109**

Replace:
```csharp
            options.AddPolicy("KnowledgeBaseUpload", policy =>
```
With:
```csharp
            options.AddPolicy(AuthorizationConstants.Policies.KnowledgeBaseUpload, policy =>
```

- [ ] **Step 3: Update `KnowledgeBaseController.cs` lines 50 and 58**

Replace both occurrences of:
```csharp
    [Authorize(Policy = "KnowledgeBaseUpload")]
```
With:
```csharp
    [Authorize(Policy = AuthorizationConstants.Policies.KnowledgeBaseUpload)]
```

Note: You'll also need to add `using Anela.Heblo.Domain.Features.Authorization;` to `KnowledgeBaseController.cs` if not already present (check existing usings — it likely isn't there yet).

- [ ] **Step 4: Build the full solution to catch any missing usings**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet build --no-restore -q
```
Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
git add backend/src/Anela.Heblo.Domain/Features/Authorization/AuthorizationConstants.cs
git add backend/src/Anela.Heblo.API/Extensions/AuthenticationExtensions.cs
git add backend/src/Anela.Heblo.API/Controllers/KnowledgeBaseController.cs
git commit -m "fix: extract KnowledgeBaseUpload policy name to AuthorizationConstants.Policies"
```

---

### Task 4: Fix malformed indentation in `Anela.Heblo.Domain.csproj` (Issue 8)

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj:15`

**Background:** Line 15 has `<PackageReference ...>` at column 0 (no indentation) while all siblings are indented with 4 spaces. `dotnet format` may flag this.

- [ ] **Step 1: Fix indentation on line 15**

Replace:
```xml
<PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="8.0.0" />
```
With (4-space indent, consistent with siblings):
```xml
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="8.0.0" />
```

- [ ] **Step 2: Run dotnet format to validate**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet format src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj --verify-no-changes
```
Expected: exits 0 (no changes needed).

- [ ] **Step 3: Commit**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
git add backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj
git commit -m "fix: restore indentation in Domain.csproj PackageReference"
```

---

## Chunk 2: Configuration and resilience (Issues 3, 4)

### Task 5: Make `ResiliencePipeline` a static singleton instead of per-instance (Issue 3)

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Anthropic/AnthropicClaudeService.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.OpenAI/OpenAiEmbeddingService.cs`

**Background:** Both services build an identical `ResiliencePipeline` in their constructors. Since services are registered as `Scoped`, the pipeline is rebuilt on every request. Pipelines are stateless and thread-safe — making them `static readonly` fields eliminates the repeated construction overhead and the DRY violation between the two files.

Note: We keep it `static readonly` (simplest, KISS) rather than injecting via DI (which would add more abstraction than needed for two callers).

- [ ] **Step 1: Edit `AnthropicClaudeService.cs` — move pipeline to static field**

Replace lines 17 and 28–37 (field declaration + constructor body that builds the pipeline):

Current constructor:
```csharp
    private readonly ResiliencePipeline _pipeline;

    public AnthropicClaudeService(
        IOptions<AnthropicOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<AnthropicClaudeService> logger)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromSeconds(2),
                BackoffType = DelayBackoffType.Exponential,
                ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>()
            })
            .Build();
    }
```

Replace with:
```csharp
    private static readonly ResiliencePipeline Pipeline = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromSeconds(2),
            BackoffType = DelayBackoffType.Exponential,
            ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>()
        })
        .Build();

    public AnthropicClaudeService(
        IOptions<AnthropicOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<AnthropicClaudeService> logger)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }
```

Also update the usage of `_pipeline` on line 76 to `Pipeline`:
```csharp
        var response = await Pipeline.ExecuteAsync(async token =>
```

- [ ] **Step 2: Edit `OpenAiEmbeddingService.cs` — same change**

Replace the instance field + constructor pipeline build:
```csharp
    private static readonly ResiliencePipeline Pipeline = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromSeconds(2),
            BackoffType = DelayBackoffType.Exponential,
            ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>()
        })
        .Build();

    public OpenAiEmbeddingService(
        IOptions<OpenAiEmbeddingOptions> options,
        ILogger<OpenAiEmbeddingService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }
```

Also update the usage on line 41 from `_pipeline` to `Pipeline`:
```csharp
        var result = await Pipeline.ExecuteAsync(
```

- [ ] **Step 3: Build both adapter projects**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet build src/Adapters/Anela.Heblo.Adapters.Anthropic/Anela.Heblo.Adapters.Anthropic.csproj --no-restore -q
dotnet build src/Adapters/Anela.Heblo.Adapters.OpenAI/Anela.Heblo.Adapters.OpenAI.csproj --no-restore -q
```
Expected: `Build succeeded.` for both.

- [ ] **Step 4: Commit**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
git add backend/src/Adapters/Anela.Heblo.Adapters.Anthropic/AnthropicClaudeService.cs
git add backend/src/Adapters/Anela.Heblo.Adapters.OpenAI/OpenAiEmbeddingService.cs
git commit -m "fix: make ResiliencePipeline a static readonly field to avoid rebuilding per scope"
```

---

### Task 6: Add `BaseUrl` to `AnthropicOptions` and use it in the service (Issue 4)

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Anthropic/AnthropicOptions.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Anthropic/AnthropicClaudeService.cs:79`
- Modify: `backend/src/Anela.Heblo.API/appsettings.json`

**Background:** The URL `"https://api.anthropic.com/v1/messages"` is hardcoded in `AnthropicClaudeService`. Adding it to `AnthropicOptions` allows overriding it via configuration (e.g., for a corporate proxy, or in tests). Default stays unchanged.

- [ ] **Step 1: Add `MessagesUrl` property to `AnthropicOptions.cs`**

```csharp
public class AnthropicOptions
{
    public const string SectionKey = "Anthropic";

    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "claude-sonnet-4-6";
    public int MaxTokens { get; set; } = 1024;
    public string MessagesUrl { get; set; } = "https://api.anthropic.com/v1/messages";
}
```

- [ ] **Step 2: Use `_options.MessagesUrl` in `AnthropicClaudeService.cs` line 79**

Replace:
```csharp
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
```
With:
```csharp
            using var request = new HttpRequestMessage(HttpMethod.Post, _options.MessagesUrl);
```

- [ ] **Step 3: Build**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet build src/Adapters/Anela.Heblo.Adapters.Anthropic/Anela.Heblo.Adapters.Anthropic.csproj --no-restore -q
```
Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
git add backend/src/Adapters/Anela.Heblo.Adapters.Anthropic/AnthropicOptions.cs
git add backend/src/Adapters/Anela.Heblo.Adapters.Anthropic/AnthropicClaudeService.cs
git commit -m "fix: move hardcoded Anthropic API URL to AnthropicOptions.MessagesUrl"
```

---

## Chunk 3: Type safety (Issue 7)

### Task 7: Replace `DocumentStatus` string constants with an enum (Issue 7)

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/KnowledgeBase/KnowledgeBaseDocument.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/KnowledgeBase/KnowledgeBaseDocumentConfiguration.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/IndexDocument/IndexDocumentHandler.cs` (Status assignments)
- Modify: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentHandler.cs` (Status assignments)
- Modify: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/GetDocuments/GetDocumentsRequest.cs` (DocumentSummary.Status type)
- Modify: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/GetDocuments/GetDocumentsHandler.cs` (mapping)

**Background:** `DocumentStatus` is currently a `static class` with `const string` values. This allows assigning any string to `KnowledgeBaseDocument.Status` without a compile error. The DB stores lowercase strings (`"processing"`, `"indexed"`, `"failed"`). We change to an enum and configure EF Core to store it as a lowercase string via a custom converter so DB data remains compatible — **no migration needed**.

`DocumentSummary.Status` is a `string` exposed in the API response. We keep that as `string` (serialize from enum) so the OpenAPI contract doesn't change.

- [ ] **Step 1: Replace `DocumentStatus` static class with enum in `KnowledgeBaseDocument.cs`**

```csharp
namespace Anela.Heblo.Domain.Features.KnowledgeBase;

public class KnowledgeBaseDocument
{
    public Guid Id { get; set; }
    public string Filename { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty; // SHA-256 hex, 64 chars
    public DocumentStatus Status { get; set; } = DocumentStatus.Processing;
    public DateTime CreatedAt { get; set; }
    public DateTime? IndexedAt { get; set; }

    public ICollection<KnowledgeBaseChunk> Chunks { get; set; } = new List<KnowledgeBaseChunk>();
}

public enum DocumentStatus
{
    Processing,
    Indexed,
    Failed
}
```

- [ ] **Step 2: Add enum-to-lowercase-string converter in `KnowledgeBaseDocumentConfiguration.cs`**

Add a `HasConversion` call for the `Status` property so EF Core stores `"processing"` / `"indexed"` / `"failed"` (lowercase), matching existing DB values:

```csharp
        builder.Property(e => e.Status)
            .IsRequired()
            .HasMaxLength(50)
            .HasConversion(
                v => v.ToString().ToLowerInvariant(),
                v => Enum.Parse<DocumentStatus>(v, ignoreCase: true));
```

Replace the existing:
```csharp
        builder.Property(e => e.Status)
            .IsRequired()
            .HasMaxLength(50);
```

- [ ] **Step 3: Fix Status assignments in `IndexDocumentHandler.cs`**

The assignments `document.Status = DocumentStatus.Processing;` etc. already use the `DocumentStatus` class — after the type change to enum, they compile as-is because the enum member names match the old const names. Verify the file builds without changes (step 5).

- [ ] **Step 4: Fix `DocumentSummary.Status` in `GetDocumentsRequest.cs` — keep as string, map from enum**

`DocumentSummary.Status` is `string` (API response DTO — must stay `string` per project DTO rules). The mapping in `GetDocumentsHandler.cs` currently does:
```csharp
Status = d.Status,
```
Since `d.Status` is now `DocumentStatus` (enum) and `DocumentSummary.Status` is `string`, this won't compile. Fix the mapping:
```csharp
Status = d.Status.ToString().ToLowerInvariant(),
```

- [ ] **Step 5: Build the full solution**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet build --no-restore -q
```
Expected: `Build succeeded.` with no errors.

If there are compilation errors in other files referencing `DocumentStatus`, fix them the same way: replace string comparisons with enum member comparisons, and string assignments with enum value assignments.

- [ ] **Step 6: Run backend unit tests**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "Category!=Integration" --no-build -q
```
Expected: all tests pass.

- [ ] **Step 7: Commit**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
git add backend/src/Anela.Heblo.Domain/Features/KnowledgeBase/KnowledgeBaseDocument.cs
git add backend/src/Anela.Heblo.Persistence/KnowledgeBase/KnowledgeBaseDocumentConfiguration.cs
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/GetDocuments/GetDocumentsHandler.cs
git commit -m "fix: change DocumentStatus from string constants to enum with lowercase EF Core converter"
```

---

## Chunk 4: DRY — extract shared indexing logic (Issue 1)

### Task 8: Extract chunk-embedding loop into `IDocumentIndexingService` (Issue 1)

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/IDocumentIndexingService.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/DocumentIndexingService.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/IndexDocument/IndexDocumentHandler.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentHandler.cs`

**Background:** Both `IndexDocumentHandler` and `UploadDocumentHandler` contain an identical ~30-line loop: extract text → chunk → embed each chunk → persist chunks → update document status. This loop is extracted into `DocumentIndexingService.IndexChunksAsync(byte[] content, string contentType, KnowledgeBaseDocument doc, CancellationToken ct)`. Both handlers delegate to it.

The `SaveChangesAsync` call **after** the loop stays in the handlers, because each has different transactional semantics (UploadDocument wraps in try/catch to mark as Failed; IndexDocument doesn't).

- [ ] **Step 1: Write the failing test for `DocumentIndexingService`**

Create `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/DocumentIndexingServiceTests.cs`:

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase;
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.Services;

public class DocumentIndexingServiceTests
{
    private readonly Mock<IDocumentTextExtractor> _pdfExtractor;
    private readonly Mock<IEmbeddingService> _embeddingService;
    private readonly Mock<IKnowledgeBaseRepository> _repository;
    private readonly DocumentIndexingService _service;

    public DocumentIndexingServiceTests()
    {
        _pdfExtractor = new Mock<IDocumentTextExtractor>();
        _pdfExtractor.Setup(e => e.CanHandle("application/pdf")).Returns(true);
        _pdfExtractor.Setup(e => e.ExtractTextAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("word1 word2 word3");

        _embeddingService = new Mock<IEmbeddingService>();
        _embeddingService.Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[] { 0.1f, 0.2f, 0.3f });

        _repository = new Mock<IKnowledgeBaseRepository>();

        var options = Options.Create(new KnowledgeBaseOptions { ChunkSize = 512, ChunkOverlapTokens = 50 });
        var chunker = new DocumentChunker(options);

        _service = new DocumentIndexingService(
            new[] { _pdfExtractor.Object },
            _embeddingService.Object,
            chunker,
            _repository.Object);
    }

    [Fact]
    public async Task IndexChunksAsync_CallsExtractorAndEmbedder_AndAddsChunks()
    {
        var doc = new KnowledgeBaseDocument { Id = Guid.NewGuid() };
        var content = new byte[] { 0x25, 0x50, 0x44, 0x46 };

        await _service.IndexChunksAsync(content, "application/pdf", doc, CancellationToken.None);

        _pdfExtractor.Verify(e => e.ExtractTextAsync(content, It.IsAny<CancellationToken>()), Times.Once);
        _embeddingService.Verify(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        _repository.Verify(r => r.AddChunksAsync(It.IsAny<IEnumerable<KnowledgeBaseChunk>>(), It.IsAny<CancellationToken>()), Times.Once);

        Assert.Equal(DocumentStatus.Indexed, doc.Status);
        Assert.NotNull(doc.IndexedAt);
    }

    [Fact]
    public async Task IndexChunksAsync_UnsupportedContentType_ThrowsNotSupportedException()
    {
        var doc = new KnowledgeBaseDocument { Id = Guid.NewGuid() };

        await Assert.ThrowsAsync<NotSupportedException>(
            () => _service.IndexChunksAsync([], "image/png", doc, CancellationToken.None));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~DocumentIndexingServiceTests" -q
```
Expected: compilation failure (`DocumentIndexingService` does not exist yet).

- [ ] **Step 3: Create `IDocumentIndexingService.cs`**

File: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/IDocumentIndexingService.cs`

```csharp
using Anela.Heblo.Domain.Features.KnowledgeBase;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public interface IDocumentIndexingService
{
    /// <summary>
    /// Extracts text from <paramref name="content"/>, chunks it, generates embeddings,
    /// persists the chunks, and marks <paramref name="document"/> as Indexed.
    /// Does NOT call SaveChanges — caller is responsible.
    /// Throws <see cref="NotSupportedException"/> if no extractor handles <paramref name="contentType"/>.
    /// </summary>
    Task IndexChunksAsync(
        byte[] content,
        string contentType,
        KnowledgeBaseDocument document,
        CancellationToken ct = default);
}
```

- [ ] **Step 4: Create `DocumentIndexingService.cs`**

File: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/DocumentIndexingService.cs`

```csharp
using Anela.Heblo.Domain.Features.KnowledgeBase;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public class DocumentIndexingService : IDocumentIndexingService
{
    private readonly IEnumerable<IDocumentTextExtractor> _extractors;
    private readonly IEmbeddingService _embeddingService;
    private readonly DocumentChunker _chunker;
    private readonly IKnowledgeBaseRepository _repository;

    public DocumentIndexingService(
        IEnumerable<IDocumentTextExtractor> extractors,
        IEmbeddingService embeddingService,
        DocumentChunker chunker,
        IKnowledgeBaseRepository repository)
    {
        _extractors = extractors;
        _embeddingService = embeddingService;
        _chunker = chunker;
        _repository = repository;
    }

    public async Task IndexChunksAsync(
        byte[] content,
        string contentType,
        KnowledgeBaseDocument document,
        CancellationToken ct = default)
    {
        var extractor = _extractors.FirstOrDefault(e => e.CanHandle(contentType))
            ?? throw new NotSupportedException($"Content type '{contentType}' is not supported.");

        var text = await extractor.ExtractTextAsync(content, ct);
        var chunkTexts = _chunker.Chunk(text);

        var chunks = new List<KnowledgeBaseChunk>();
        for (var i = 0; i < chunkTexts.Count; i++)
        {
            var embedding = await _embeddingService.GenerateEmbeddingAsync(chunkTexts[i], ct);
            chunks.Add(new KnowledgeBaseChunk
            {
                Id = Guid.NewGuid(),
                DocumentId = document.Id,
                ChunkIndex = i,
                Content = chunkTexts[i],
                Embedding = embedding,
            });
        }

        await _repository.AddChunksAsync(chunks, ct);

        document.Status = DocumentStatus.Indexed;
        document.IndexedAt = DateTime.UtcNow;
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~DocumentIndexingServiceTests" -q
```
Expected: 2 tests pass.

- [ ] **Step 6: Register `DocumentIndexingService` in `KnowledgeBaseModule.cs`**

In `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs`, add after the `DocumentChunker` registration:

```csharp
        services.AddScoped<IDocumentIndexingService, DocumentIndexingService>();
```

- [ ] **Step 7: Refactor `IndexDocumentHandler.cs` to use `IDocumentIndexingService`**

Replace the entire class with:

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.IndexDocument;

public class IndexDocumentHandler : IRequestHandler<IndexDocumentRequest>
{
    private readonly IKnowledgeBaseRepository _repository;
    private readonly IDocumentIndexingService _indexingService;
    private readonly ILogger<IndexDocumentHandler> _logger;

    public IndexDocumentHandler(
        IKnowledgeBaseRepository repository,
        IDocumentIndexingService indexingService,
        ILogger<IndexDocumentHandler> logger)
    {
        _repository = repository;
        _indexingService = indexingService;
        _logger = logger;
    }

    public async Task Handle(IndexDocumentRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Indexing document {Filename} from {SourcePath}", request.Filename, request.SourcePath);

        var document = new KnowledgeBaseDocument
        {
            Id = Guid.NewGuid(),
            Filename = request.Filename,
            SourcePath = request.SourcePath,
            ContentType = request.ContentType,
            ContentHash = request.ContentHash,
            Status = DocumentStatus.Processing,
            CreatedAt = DateTime.UtcNow
        };

        await _repository.AddDocumentAsync(document, cancellationToken);

        await _indexingService.IndexChunksAsync(request.Content, request.ContentType, document, cancellationToken);

        await _repository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Indexed document {Filename}", request.Filename);
    }
}
```

- [ ] **Step 8: Refactor `UploadDocumentHandler.cs` to use `IDocumentIndexingService`**

Replace the injected `IEnumerable<IDocumentTextExtractor>`, `DocumentChunker`, and `IEmbeddingService` with `IDocumentIndexingService`. Keep the `ResolveContentType` helper and the `extractor` availability pre-check (which must stay in the handler because it needs to happen _before_ persisting the document):

```csharp
using System.Security.Cryptography;
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.GetDocuments;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using MediatR;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.UploadDocument;

public class UploadDocumentHandler : IRequestHandler<UploadDocumentRequest, UploadDocumentResponse>
{
    private readonly IKnowledgeBaseRepository _repository;
    private readonly IEnumerable<IDocumentTextExtractor> _extractors;
    private readonly IDocumentIndexingService _indexingService;

    public UploadDocumentHandler(
        IKnowledgeBaseRepository repository,
        IEnumerable<IDocumentTextExtractor> extractors,
        IDocumentIndexingService indexingService)
    {
        _repository = repository;
        _extractors = extractors;
        _indexingService = indexingService;
    }

    public async Task<UploadDocumentResponse> Handle(
        UploadDocumentRequest request,
        CancellationToken cancellationToken)
    {
        using var ms = new MemoryStream();
        await request.FileStream.CopyToAsync(ms, cancellationToken);
        var fileBytes = ms.ToArray();

        var hash = Convert.ToHexString(SHA256.HashData(fileBytes));
        var existing = await _repository.GetDocumentByHashAsync(hash, cancellationToken);
        if (existing != null)
        {
            return new UploadDocumentResponse { Document = MapToSummary(existing) };
        }

        var contentType = ResolveContentType(request.ContentType, request.Filename);

        // Validate extractor availability before persisting anything
        if (!_extractors.Any(e => e.CanHandle(contentType)))
        {
            return new UploadDocumentResponse
            {
                Success = false,
                ErrorCode = ErrorCodes.UnsupportedFileType,
            };
        }

        var doc = new KnowledgeBaseDocument
        {
            Id = Guid.NewGuid(),
            Filename = request.Filename,
            SourcePath = $"upload/{Guid.NewGuid()}/{request.Filename}",
            ContentType = contentType,
            ContentHash = hash,
            Status = DocumentStatus.Processing,
            CreatedAt = DateTime.UtcNow,
        };
        await _repository.AddDocumentAsync(doc, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        try
        {
            await _indexingService.IndexChunksAsync(fileBytes, contentType, doc, cancellationToken);
            await _repository.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            doc.Status = DocumentStatus.Failed;
            await _repository.SaveChangesAsync(cancellationToken);
            throw;
        }

        return new UploadDocumentResponse { Document = MapToSummary(doc) };
    }

    private static DocumentSummary MapToSummary(KnowledgeBaseDocument doc) =>
        new()
        {
            Id = doc.Id,
            Filename = doc.Filename,
            Status = doc.Status.ToString().ToLowerInvariant(),
            ContentType = doc.ContentType,
            CreatedAt = doc.CreatedAt,
            IndexedAt = doc.IndexedAt,
        };

    private static string ResolveContentType(string contentType, string filename) =>
        string.IsNullOrEmpty(contentType) || contentType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase)
            ? Path.GetExtension(filename).ToLowerInvariant() switch
            {
                ".pdf" => "application/pdf",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".doc" => "application/msword",
                ".txt" => "text/plain",
                ".md" => "text/markdown",
                _ => contentType
            }
            : contentType;
}
```

- [ ] **Step 9: Build the full solution**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet build --no-restore -q
```
Expected: `Build succeeded.`

- [ ] **Step 10: Run all unit tests**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "Category!=Integration" -q
```
Expected: all tests pass.

- [ ] **Step 11: Commit**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/IDocumentIndexingService.cs
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/DocumentIndexingService.cs
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/IndexDocument/IndexDocumentHandler.cs
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentHandler.cs
git add backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/DocumentIndexingServiceTests.cs
git commit -m "refactor: extract shared chunk-embed-persist loop into DocumentIndexingService (DRY)"
```

---

## Final: Push all commits

- [ ] **Push branch to origin**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
git push origin feat/381-rag-knowledge-base
```

---

## Summary of changes by file

| File | Action | Issue |
|------|--------|-------|
| `Persistence/KnowledgeBase/KnowledgeBaseRepository.cs` | Remove `async`/`await Task.CompletedTask` | #2 |
| `Application/.../AskQuestion/AskQuestionHandler.cs` | Rename `_claude` → `_answerService` | #5 |
| `Domain/Features/Authorization/AuthorizationConstants.cs` | Add `Policies.KnowledgeBaseUpload` constant | #6 |
| `API/Extensions/AuthenticationExtensions.cs` | Use constant instead of string literal | #6 |
| `API/Controllers/KnowledgeBaseController.cs` | Use constant instead of string literal | #6 |
| `Domain/Anela.Heblo.Domain.csproj` | Fix indentation on line 15 | #8 |
| `Adapters.Anthropic/AnthropicClaudeService.cs` | Make pipeline `static readonly` | #3 |
| `Adapters.OpenAI/OpenAiEmbeddingService.cs` | Make pipeline `static readonly` | #3 |
| `Adapters.Anthropic/AnthropicOptions.cs` | Add `MessagesUrl` property | #4 |
| `Adapters.Anthropic/AnthropicClaudeService.cs` | Use `_options.MessagesUrl` | #4 |
| `Domain/Features/KnowledgeBase/KnowledgeBaseDocument.cs` | Change `DocumentStatus` to enum | #7 |
| `Persistence/.../KnowledgeBaseDocumentConfiguration.cs` | Add enum-to-string EF Core converter | #7 |
| `Application/.../GetDocuments/GetDocumentsHandler.cs` | Map enum status to lowercase string | #7 |
| `Application/.../Services/IDocumentIndexingService.cs` | **New** — interface | #1 |
| `Application/.../Services/DocumentIndexingService.cs` | **New** — implementation | #1 |
| `Application/.../KnowledgeBaseModule.cs` | Register new service | #1 |
| `Application/.../IndexDocument/IndexDocumentHandler.cs` | Delegate to service | #1 |
| `Application/.../UploadDocument/UploadDocumentHandler.cs` | Delegate to service | #1 |
| `Tests/.../Services/DocumentIndexingServiceTests.cs` | **New** — unit tests | #1 |
