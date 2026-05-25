# Decouple Article Module from KnowledgeBase — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `GatherContextStep`'s direct dependency on KnowledgeBase's `IOneDriveService` with a consumer-owned contract `IArticleStyleGuideSource` defined in the Article module and implemented by a KnowledgeBase adapter, plus an architecture test that locks the new boundary in place.

**Architecture:** Mirror of the existing `ILeafletKnowledgeSource` precedent. Article declares the narrow contract it needs (download a style guide text by drive id and path). KnowledgeBase ships an `internal sealed` adapter that delegates to its existing `IOneDriveService`. KnowledgeBase's composition root wires the binding so Article's DI graph stays free of any KnowledgeBase symbols. A new `ModuleBoundaryRule` row in `ModuleBoundariesTests.Rules()` enforces the invariant — its allowlist temporarily covers the surviving `SearchDocumentsRequest`/`SearchDocumentsResponse`/`ChunkResult` references in `GatherContextStep` which are explicitly out of scope per the spec.

**Tech Stack:** .NET 8, C# nullable reference types, xUnit, FluentAssertions, Moq, MediatR, `Microsoft.Extensions.DependencyInjection`, `System.Reflection`. No new NuGet packages, no migrations, no configuration changes.

---

## File Structure

### New files
| Path | Responsibility |
|---|---|
| `backend/src/Anela.Heblo.Application/Features/Article/Contracts/IArticleStyleGuideSource.cs` | Article-owned read-only abstraction for downloading the style guide text. Only surface the Article module is allowed to depend on for this concern. |
| `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/KnowledgeBaseArticleStyleGuideSource.cs` | `internal sealed` adapter implementing `IArticleStyleGuideSource` by delegating verbatim to `IOneDriveService.DownloadFileTextByPathAsync`. |
| `backend/test/Anela.Heblo.Tests/Features/KnowledgeBase/Infrastructure/KnowledgeBaseArticleStyleGuideSourceTests.cs` | Unit tests for the adapter: happy-path delegation, cancellation propagation, exception propagation. |

### Files modified — behavior change
| Path | Change |
|---|---|
| `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/GatherContextStep.cs` | Drop `using Anela.Heblo.Application.Features.KnowledgeBase.Services;`. Swap `IOneDriveService _oneDrive` for `IArticleStyleGuideSource _styleGuideSource`. Call `_styleGuideSource.DownloadStyleGuideTextAsync(...)` inside `LoadStyleGuideAsync`. `using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;` is **kept** — `SearchDocumentsRequest`/`SearchDocumentsResponse`/`ChunkResult` are explicitly out of scope (spec §Out of Scope; arch-review §Specification Amendments). |
| `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs` | Register `services.AddScoped<IArticleStyleGuideSource, KnowledgeBaseArticleStyleGuideSource>();` immediately after the existing `ILeafletKnowledgeSource` binding. |

### Files modified — tests
| Path | Change |
|---|---|
| `backend/test/Anela.Heblo.Tests/Article/Pipeline/GatherContextStepTests.cs` | Drop `using Anela.Heblo.Application.Features.KnowledgeBase.Services;`. Replace `Mock<IOneDriveService> _oneDrive` with `Mock<IArticleStyleGuideSource> _styleGuideSource`. Wire `_styleGuideSource.Object` into the `CreateStep` factory. Add a happy-path test that exercises `LoadStyleGuideAsync` through the new contract. The file may keep `using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;` because the test project is not under the architecture rule and `SearchDocumentsRequest` is still consumed by the step under test. |
| `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` | Add `ArticleAllowlist` (`HashSet<string>`) with the three `SearchDocuments` entries documented in arch-review §Specification Amendments. Append one new `ModuleBoundaryRule` to `Rules()` TheoryData: `Name: "Article -> KnowledgeBase"`, `InspectedNamespacePrefix: "Anela.Heblo.Application.Features.Article"`, three forbidden namespace prefixes covering `Domain.Features.KnowledgeBase`, `Application.Features.KnowledgeBase`, and `Persistence.KnowledgeBase`, `Allowlist: ArticleAllowlist`. |

### Files NOT modified
- `backend/src/Anela.Heblo.Application/Features/Article/ArticleModule.cs` — must not gain any reference to `OneDriveService`, the new adapter, or any KnowledgeBase type. Leave untouched.
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/IOneDriveService.cs` and its implementations — unchanged.
- Any EF migration, controller, or frontend file — none of this touches API/HTTP/OpenAPI surface or persistence.

---

## Task 1: Define the consumer-owned contract `IArticleStyleGuideSource`

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Article/Contracts/IArticleStyleGuideSource.cs`

- [ ] **Step 1.1: Create the new file**

Write `backend/src/Anela.Heblo.Application/Features/Article/Contracts/IArticleStyleGuideSource.cs` with this exact content:

```csharp
namespace Anela.Heblo.Application.Features.Article.Contracts;

/// <summary>
/// Retrieves the Article module's style guide text from an external source.
/// Implemented by the KnowledgeBase module via an adapter.
/// </summary>
public interface IArticleStyleGuideSource
{
    Task<string> DownloadStyleGuideTextAsync(
        string driveId,
        string path,
        CancellationToken cancellationToken);
}
```

Constraints — verify before moving on:
- Namespace is exactly `Anela.Heblo.Application.Features.Article.Contracts`.
- File contains zero `using` directives referencing `Anela.Heblo.Application.Features.KnowledgeBase`, `Anela.Heblo.Domain.Features.KnowledgeBase`, or `Anela.Heblo.Persistence.KnowledgeBase`.
- File contains zero references to `IOneDriveService`, `OneDriveFile`, or any other KnowledgeBase-owned type.
- Method signature matches the call site in `GatherContextStep.LoadStyleGuideAsync` — three parameters in the order `driveId`, `path`, `cancellationToken`, returning `Task<string>` (not `Task<string?>` — the underlying `IOneDriveService.DownloadFileTextByPathAsync` returns non-null `Task<string>`; the step decides whether to wrap with `null` on exception, not the contract).

- [ ] **Step 1.2: Compile the solution to confirm the file is well-formed**

Run:

```bash
cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: build succeeds with no errors. The new interface has no implementers yet so DI will not resolve it, but compilation must pass.

- [ ] **Step 1.3: Commit**

```bash
cd backend && git add src/Anela.Heblo.Application/Features/Article/Contracts/IArticleStyleGuideSource.cs
git commit -m "feat(article): add IArticleStyleGuideSource consumer-owned contract"
```

---

## Task 2: Write the adapter test (RED)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/KnowledgeBase/Infrastructure/KnowledgeBaseArticleStyleGuideSourceTests.cs`

The adapter does three things: forwards arguments verbatim to `IOneDriveService.DownloadFileTextByPathAsync`, returns its result unchanged, and lets exceptions/cancellation propagate. One test per behavior. Tests go first; the adapter implementation follows in Task 3.

- [ ] **Step 2.1: Verify the test directory exists; create if missing**

Run:

```bash
cd backend && mkdir -p test/Anela.Heblo.Tests/Features/KnowledgeBase/Infrastructure
```

Expected: no error (directory either pre-existed or was created).

- [ ] **Step 2.2: Write the failing adapter test file**

Create `backend/test/Anela.Heblo.Tests/Features/KnowledgeBase/Infrastructure/KnowledgeBaseArticleStyleGuideSourceTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Article.Contracts;
using Anela.Heblo.Application.Features.KnowledgeBase.Infrastructure;
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.KnowledgeBase.Infrastructure;

public class KnowledgeBaseArticleStyleGuideSourceTests
{
    private readonly Mock<IOneDriveService> _oneDrive = new();

    private IArticleStyleGuideSource CreateSut() =>
        new KnowledgeBaseArticleStyleGuideSource(_oneDrive.Object);

    [Fact]
    public async Task DownloadStyleGuideTextAsync_ForwardsArgumentsAndReturnsResult()
    {
        const string driveId = "drive-abc";
        const string path = "/style-guides/article.md";
        const string expected = "guide body";

        _oneDrive
            .Setup(o => o.DownloadFileTextByPathAsync(driveId, path, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var sut = CreateSut();

        var actual = await sut.DownloadStyleGuideTextAsync(driveId, path, CancellationToken.None);

        actual.Should().Be(expected);
        _oneDrive.Verify(
            o => o.DownloadFileTextByPathAsync(driveId, path, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task DownloadStyleGuideTextAsync_PropagatesUnderlyingException()
    {
        _oneDrive
            .Setup(o => o.DownloadFileTextByPathAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("OneDrive down"));

        var sut = CreateSut();

        var act = () => sut.DownloadStyleGuideTextAsync("drive", "path", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("OneDrive down");
    }

    [Fact]
    public async Task DownloadStyleGuideTextAsync_PropagatesCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        _oneDrive
            .Setup(o => o.DownloadFileTextByPathAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var sut = CreateSut();

        var act = () => sut.DownloadStyleGuideTextAsync("drive", "path", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
```

- [ ] **Step 2.3: Run the new tests to confirm they fail with "type does not exist"**

Run:

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~KnowledgeBaseArticleStyleGuideSourceTests"
```

Expected: compile error `CS0246: The type or namespace name 'KnowledgeBaseArticleStyleGuideSource' could not be found`. This proves the test is referencing the concrete type by name and will fail if Task 3 produces the wrong shape.

> Do not commit yet — failing tests get committed together with their implementation in Task 3.

---

## Task 3: Implement `KnowledgeBaseArticleStyleGuideSource` (GREEN)

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/KnowledgeBaseArticleStyleGuideSource.cs`

- [ ] **Step 3.1: Create the adapter file**

Write `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/KnowledgeBaseArticleStyleGuideSource.cs`:

```csharp
using Anela.Heblo.Application.Features.Article.Contracts;
using Anela.Heblo.Application.Features.KnowledgeBase.Services;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Infrastructure;

internal sealed class KnowledgeBaseArticleStyleGuideSource : IArticleStyleGuideSource
{
    private readonly IOneDriveService _oneDrive;

    public KnowledgeBaseArticleStyleGuideSource(IOneDriveService oneDrive)
    {
        _oneDrive = oneDrive;
    }

    public Task<string> DownloadStyleGuideTextAsync(
        string driveId,
        string path,
        CancellationToken cancellationToken) =>
        _oneDrive.DownloadFileTextByPathAsync(driveId, path, cancellationToken);
}
```

Constraints to verify:
- Class is `internal sealed` (matches `KnowledgeBaseLeafletSourceAdapter`).
- Constructor takes a single `IOneDriveService` dependency (matches existing adapter pattern).
- Method body is a one-line expression-bodied member that forwards all three arguments verbatim. No transformation, no logging, no try/catch — the calling step already handles exceptions.

- [ ] **Step 3.2: Run the adapter tests — expect green**

Run:

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~KnowledgeBaseArticleStyleGuideSourceTests"
```

Expected: all three tests pass. `DownloadStyleGuideTextAsync_ForwardsArgumentsAndReturnsResult`, `..._PropagatesUnderlyingException`, `..._PropagatesCancellation`.

- [ ] **Step 3.3: Commit adapter + its tests together**

```bash
cd backend && git add \
  src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/KnowledgeBaseArticleStyleGuideSource.cs \
  test/Anela.Heblo.Tests/Features/KnowledgeBase/Infrastructure/KnowledgeBaseArticleStyleGuideSourceTests.cs
git commit -m "feat(knowledgebase): add KnowledgeBaseArticleStyleGuideSource adapter"
```

---

## Task 4: Register the adapter in `KnowledgeBaseModule`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs`

- [ ] **Step 4.1: Add the using directive for the Article contract**

Open `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs`. The existing using block already imports `Anela.Heblo.Application.Features.Leaflet.Contracts`. Add the Article contracts import directly below it.

Find:

```csharp
using Anela.Heblo.Application.Features.Leaflet.Contracts;
using Anela.Heblo.Application.Features.KnowledgeBase.Infrastructure;
```

Replace with:

```csharp
using Anela.Heblo.Application.Features.Article.Contracts;
using Anela.Heblo.Application.Features.Leaflet.Contracts;
using Anela.Heblo.Application.Features.KnowledgeBase.Infrastructure;
```

(Article comes first alphabetically — match the rest of the file's alphabetical-within-block ordering.)

- [ ] **Step 4.2: Register the binding next to the Leaflet binding**

In the same file, find the existing Leaflet registration block:

```csharp
        // Cross-module contract: KnowledgeBase implements Leaflet's ILeafletKnowledgeSource via adapter.
        // DI registration owned by provider (KnowledgeBase), not consumer (Leaflet) — keeps the
        // dependency direction inverted properly.
        services.AddScoped<ILeafletKnowledgeSource, KnowledgeBaseLeafletSourceAdapter>();
```

Replace with:

```csharp
        // Cross-module contract: KnowledgeBase implements Leaflet's ILeafletKnowledgeSource via adapter.
        // DI registration owned by provider (KnowledgeBase), not consumer (Leaflet) — keeps the
        // dependency direction inverted properly.
        services.AddScoped<ILeafletKnowledgeSource, KnowledgeBaseLeafletSourceAdapter>();

        // Cross-module contract: KnowledgeBase implements Article's IArticleStyleGuideSource via adapter.
        // Same provider-owned-DI pattern as the Leaflet binding above.
        services.AddScoped<IArticleStyleGuideSource, KnowledgeBaseArticleStyleGuideSource>();
```

Constraints — verify:
- Lifetime is `AddScoped` (matches `ILeafletKnowledgeSource` registration and the lifetime of the underlying `IOneDriveService`, which is also `Scoped`).
- The Article module's composition root (`ArticleModule.cs`) is **not** modified — the binding lives in the provider's module by design.

- [ ] **Step 4.3: Build to confirm the wiring compiles**

Run:

```bash
cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: build succeeds.

- [ ] **Step 4.4: Commit**

```bash
cd backend && git add src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs
git commit -m "feat(knowledgebase): register IArticleStyleGuideSource adapter"
```

---

## Task 5: Update `GatherContextStepTests` to drive the contract swap (RED)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Article/Pipeline/GatherContextStepTests.cs`

The existing test file uses `Mock<IOneDriveService>` but no current test actually exercises `LoadStyleGuideAsync`. After this task: (a) the existing tests still pass (they don't touch the style guide path), (b) the test file no longer imports `IOneDriveService`, (c) a new test locks the happy-path wiring through `IArticleStyleGuideSource`. This drives the production refactor in Task 6.

- [ ] **Step 5.1: Replace the `IOneDriveService` mock with `IArticleStyleGuideSource`**

Open `backend/test/Anela.Heblo.Tests/Article/Pipeline/GatherContextStepTests.cs`.

Find:

```csharp
using Anela.Heblo.Application.Features.Article;
using Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline;
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;
```

Replace with:

```csharp
using Anela.Heblo.Application.Features.Article;
using Anela.Heblo.Application.Features.Article.Contracts;
using Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;
```

(The `KnowledgeBase.Services` using is removed. `KnowledgeBase.UseCases.SearchDocuments` stays — `SearchDocumentsRequest`/`SearchDocumentsResponse`/`ChunkResult` remain in use by the step and the test, per spec §Out of Scope.)

Find:

```csharp
    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<IWebSearchClient> _webSearch = new();
    private readonly Mock<IOneDriveService> _oneDrive = new();
    private readonly ArticleOptions _options = new();
```

Replace with:

```csharp
    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<IWebSearchClient> _webSearch = new();
    private readonly Mock<IArticleStyleGuideSource> _styleGuideSource = new();
    private readonly ArticleOptions _options = new();
```

Find:

```csharp
    private GatherContextStep CreateStep() =>
        new(_mediator.Object, _webSearch.Object, _oneDrive.Object,
            Options.Create(_options), NullLogger<GatherContextStep>.Instance, CreateNoOpRecorder());
```

Replace with:

```csharp
    private GatherContextStep CreateStep() =>
        new(_mediator.Object, _webSearch.Object, _styleGuideSource.Object,
            Options.Create(_options), NullLogger<GatherContextStep>.Instance, CreateNoOpRecorder());
```

- [ ] **Step 5.2: Add a happy-path test that exercises the style guide contract**

Append this test inside `GatherContextStepTests` (after the existing `ExecuteAsync_DuplicateWebUrls_DeduplicatesByUrl` test, before the closing class brace):

```csharp
    [Fact]
    public async Task ExecuteAsync_StyleGuideConfigured_LoadsTextViaContract()
    {
        const string driveId = "drive-1";
        const string itemPath = "/style.md";
        const string guide = "Tone: friendly";

        _styleGuideSource
            .Setup(s => s.DownloadStyleGuideTextAsync(driveId, itemPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(guide);

        var article = new DomainArticle
        {
            Topic = "Topic",
            UsedKnowledgeBase = false,
            UsedWebSearch = false,
            StyleGuideDriveId = driveId,
            StyleGuideItemPath = itemPath
        };
        var context = new ArticlePipelineContext
        {
            Article = article,
            SearchQueries = new List<string>()
        };

        await CreateStep().ExecuteAsync(context, default);

        context.StyleGuideText.Should().Be(guide);
        _styleGuideSource.Verify(
            s => s.DownloadStyleGuideTextAsync(driveId, itemPath, It.IsAny<CancellationToken>()),
            Times.Once);
    }
```

- [ ] **Step 5.3: Run the tests — expect compile failure on `GatherContextStep` constructor**

Run:

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GatherContextStepTests"
```

Expected: build error — `GatherContextStep` constructor still expects `IOneDriveService`, but the test now passes `IArticleStyleGuideSource`. Error message similar to `CS1503: Argument 3: cannot convert from 'Anela.Heblo.Application.Features.Article.Contracts.IArticleStyleGuideSource' to 'Anela.Heblo.Application.Features.KnowledgeBase.Services.IOneDriveService'`. This is the expected RED state — Task 6 makes it green.

> Do not commit yet — test changes commit together with the production refactor in Task 6.

---

## Task 6: Refactor `GatherContextStep` to consume `IArticleStyleGuideSource` (GREEN)

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/GatherContextStep.cs`

- [ ] **Step 6.1: Update using directives**

Open `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/GatherContextStep.cs`.

Find:

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;
using Anela.Heblo.Application.Shared.WebSearch;
using Anela.Heblo.Domain.Features.Article;
```

Replace with:

```csharp
using Anela.Heblo.Application.Features.Article.Contracts;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;
using Anela.Heblo.Application.Shared.WebSearch;
using Anela.Heblo.Domain.Features.Article;
```

(The `KnowledgeBase.Services` using — which pulled in `IOneDriveService` — is removed. The `KnowledgeBase.UseCases.SearchDocuments` using is **kept** for `SearchDocumentsRequest`/`SearchDocumentsResponse`/`ChunkResult` per spec §Out of Scope. Article contracts using is added.)

- [ ] **Step 6.2: Swap the field and constructor parameter**

Find:

```csharp
    private readonly IMediator _mediator;
    private readonly IWebSearchClient _webSearch;
    private readonly IOneDriveService _oneDrive;
    private readonly ArticleOptions _options;
    private readonly ILogger<GatherContextStep> _logger;
    private readonly PipelineStepRecorder _recorder;

    public GatherContextStep(
        IMediator mediator,
        IWebSearchClient webSearch,
        IOneDriveService oneDrive,
        IOptions<ArticleOptions> options,
        ILogger<GatherContextStep> logger,
        PipelineStepRecorder recorder)
    {
        _mediator = mediator;
        _webSearch = webSearch;
        _oneDrive = oneDrive;
        _options = options.Value;
        _logger = logger;
        _recorder = recorder;
    }
```

Replace with:

```csharp
    private readonly IMediator _mediator;
    private readonly IWebSearchClient _webSearch;
    private readonly IArticleStyleGuideSource _styleGuideSource;
    private readonly ArticleOptions _options;
    private readonly ILogger<GatherContextStep> _logger;
    private readonly PipelineStepRecorder _recorder;

    public GatherContextStep(
        IMediator mediator,
        IWebSearchClient webSearch,
        IArticleStyleGuideSource styleGuideSource,
        IOptions<ArticleOptions> options,
        ILogger<GatherContextStep> logger,
        PipelineStepRecorder recorder)
    {
        _mediator = mediator;
        _webSearch = webSearch;
        _styleGuideSource = styleGuideSource;
        _options = options.Value;
        _logger = logger;
        _recorder = recorder;
    }
```

- [ ] **Step 6.3: Update the single call site inside `LoadStyleGuideAsync`**

Find:

```csharp
    private async Task<string?> LoadStyleGuideAsync(DomainArticle article, CancellationToken ct)
    {
        try
        {
            return await _oneDrive.DownloadFileTextByPathAsync(
                article.StyleGuideDriveId!,
                article.StyleGuideItemPath!,
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to load style guide from '{Path}'", article.StyleGuideItemPath);
            return null;
        }
    }
```

Replace with:

```csharp
    private async Task<string?> LoadStyleGuideAsync(DomainArticle article, CancellationToken ct)
    {
        try
        {
            return await _styleGuideSource.DownloadStyleGuideTextAsync(
                article.StyleGuideDriveId!,
                article.StyleGuideItemPath!,
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to load style guide from '{Path}'", article.StyleGuideItemPath);
            return null;
        }
    }
```

(Only the call target changes — argument list, ordering, cancellation handling, and warning log are preserved exactly so behavior observable from `context.StyleGuideText` is identical.)

Constraints — verify:
- `GatherContextStep.cs` no longer contains the substring `IOneDriveService` anywhere.
- `GatherContextStep.cs` no longer contains the substring `KnowledgeBase.Services` anywhere.
- `GatherContextStep.cs` *still* contains `KnowledgeBase.UseCases.SearchDocuments` (intentional — see Task 7 allowlist).
- The exception-handling block is unchanged: non-cancellation exceptions are logged at Warning and the method returns `null`; `OperationCanceledException` flows out unchanged.

- [ ] **Step 6.4: Run all `GatherContextStepTests` — expect green**

Run:

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GatherContextStepTests"
```

Expected: all six tests pass (five pre-existing + the new `ExecuteAsync_StyleGuideConfigured_LoadsTextViaContract`).

- [ ] **Step 6.5: Run the whole Article test folder to confirm no other suite regressed**

Run:

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Anela.Heblo.Tests.Article"
```

Expected: all Article-namespaced tests pass.

- [ ] **Step 6.6: Commit production refactor + test updates together**

```bash
cd backend && git add \
  src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/GatherContextStep.cs \
  test/Anela.Heblo.Tests/Article/Pipeline/GatherContextStepTests.cs
git commit -m "refactor(article): route GatherContextStep style guide load through IArticleStyleGuideSource"
```

---

## Task 7: Add the Article → KnowledgeBase architecture rule

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`

- [ ] **Step 7.1: Add `ArticleAllowlist` next to the existing per-module allowlists**

Open `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`.

Find the end of the `LeafletAllowlist` declaration (the closing `};` after the `OneDriveFile` entry). Immediately after it, insert a new `ArticleAllowlist` block:

```csharp
    // Allowlist for Article → KnowledgeBase. Each entry needs a comment with the justification.
    // Entries should be removed as the underlying violations are fixed.
    private static readonly HashSet<string> ArticleAllowlist = new(StringComparer.Ordinal)
    {
        // Pre-existing dependency: GatherContextStep dispatches SearchDocumentsRequest via MediatR
        // to obtain knowledge-base snippets during article generation. Lifting this behind a
        // consumer-owned contract (e.g. IArticleKnowledgeSearch) is out of scope for the
        // 2026-05-25 Article ↔ KnowledgeBase style-guide decoupling and is tracked as a follow-up.
        // Remove these three entries when SearchDocumentsRequest is replaced by an Article-owned
        // contract.
        "Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline.GatherContextStep -> Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments.SearchDocumentsRequest",
        "Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline.GatherContextStep -> Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments.SearchDocumentsResponse",
        "Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline.GatherContextStep -> Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments.ChunkResult",
    };
```

Placement must keep alphabetical-by-consumer-module order consistent with the existing file: `Leaflet`, then add `Article` (no — file is grouped by relevance, not alphabetically; just put `ArticleAllowlist` directly after `LeafletAllowlist` as the next logical neighbor).

- [ ] **Step 7.2: Add the `ModuleBoundaryRule` row to `Rules()` TheoryData**

In the same file, find the `Rules()` method's TheoryData initializer. The existing entries are ordered: Leaflet → KnowledgeBase, Logistics → Manufacture, PackingMaterials → Invoices, Purchase → Catalog. Add the Article → KnowledgeBase rule directly after the Leaflet rule so related rules sit together.

Find:

```csharp
    public static TheoryData<ModuleBoundaryRule> Rules() => new()
    {
        new ModuleBoundaryRule(
            Name: "Leaflet -> KnowledgeBase",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.Leaflet",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.KnowledgeBase",
                "Anela.Heblo.Application.Features.KnowledgeBase",
                "Anela.Heblo.Persistence.KnowledgeBase",
            },
            Allowlist: LeafletAllowlist),

        new ModuleBoundaryRule(
            Name: "Logistics -> Manufacture",
```

Replace with:

```csharp
    public static TheoryData<ModuleBoundaryRule> Rules() => new()
    {
        new ModuleBoundaryRule(
            Name: "Leaflet -> KnowledgeBase",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.Leaflet",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.KnowledgeBase",
                "Anela.Heblo.Application.Features.KnowledgeBase",
                "Anela.Heblo.Persistence.KnowledgeBase",
            },
            Allowlist: LeafletAllowlist),

        new ModuleBoundaryRule(
            Name: "Article -> KnowledgeBase",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.Article",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.KnowledgeBase",
                "Anela.Heblo.Application.Features.KnowledgeBase",
                "Anela.Heblo.Persistence.KnowledgeBase",
            },
            Allowlist: ArticleAllowlist),

        new ModuleBoundaryRule(
            Name: "Logistics -> Manufacture",
```

Constraints — verify:
- `InspectedNamespacePrefix` is exactly `Anela.Heblo.Application.Features.Article` (no trailing dot — the existing helper handles both equality and `prefix + "."` start).
- All three forbidden prefixes are present — copy them from the Leaflet rule above to avoid typos.
- `Allowlist: ArticleAllowlist` references the new constant, not `LeafletAllowlist`.

- [ ] **Step 7.3: Run the architecture test for the new rule**

Run:

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ModuleBoundariesTests"
```

Expected: all `ModuleBoundariesTests` theories pass, including the new `Consumer_types_should_not_reference_provider_owned_namespaces(rule: Article -> KnowledgeBase)` case.

If this fails with a violation other than the three allowlisted `SearchDocuments` entries, it means another `Anela.Heblo.Application.Features.Article` type still references a KnowledgeBase-owned namespace. Per spec §Out of Scope, do **not** silently expand scope or add more allowlist entries — surface the finding to the reviewer with the exact `Consumer -> Provider` line from the violation message so it can be tracked as a follow-up.

- [ ] **Step 7.4: Commit**

```bash
cd backend && git add test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
git commit -m "test(architecture): enforce Article -> KnowledgeBase boundary with SearchDocuments allowlist"
```

---

## Task 8: Full validation

**Files:** none modified — verification only.

- [ ] **Step 8.1: Run the full backend test suite**

Run:

```bash
cd backend && dotnet test
```

Expected: every test project passes. If any pre-existing test fails, stop and investigate — the refactor in this plan must be behavior-preserving outside the swapped DI seam.

- [ ] **Step 8.2: Build the full solution**

Run:

```bash
cd backend && dotnet build
```

Expected: zero errors. Warning count should not increase vs. the pre-refactor baseline (`git stash && dotnet build` on the parent commit if a baseline comparison is needed).

- [ ] **Step 8.3: Apply formatter**

Run:

```bash
cd backend && dotnet format
```

Expected: command completes; any files it rewrites should be limited to the ones touched in this plan. If unrelated files change, revert those — surgical-change rule applies.

- [ ] **Step 8.4: Commit any formatter changes (only if dotnet format modified files in this PR's diff)**

```bash
cd backend && git status
# if there are changes:
git add -A
git commit -m "style: apply dotnet format"
```

(Skip if `git status` is clean.)

- [ ] **Step 8.5: Sanity-check the final boundary**

Run two `grep`-style scans to confirm the refactor sticks:

```bash
cd backend && grep -rn "IOneDriveService\|KnowledgeBase.Services" src/Anela.Heblo.Application/Features/Article/
```

Expected: zero matches.

```bash
cd backend && grep -n "IArticleStyleGuideSource" src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/GatherContextStep.cs
```

Expected: at least three matches (using directive, field type, constructor parameter type — plus method call inside `LoadStyleGuideAsync`).

```bash
cd backend && grep -n "IArticleStyleGuideSource" src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs
```

Expected: at least two matches (using directive + `AddScoped<...>` line).

---

## Self-Review

**Spec coverage** — every functional requirement maps to at least one task:

| Requirement | Tasks |
|---|---|
| FR-1: Define `IArticleStyleGuideSource` in Article/Contracts | Task 1 |
| FR-2: KnowledgeBase adapter implementing the contract | Tasks 2, 3 |
| FR-3: DI registration owned by KnowledgeBase, same lifetime as Leaflet | Task 4 |
| FR-4: `GatherContextStep` swaps to new contract, no `KnowledgeBase.Services` import | Tasks 5, 6 (arch-review §Amendment 4 acknowledged — `KnowledgeBase.UseCases.SearchDocuments` using stays) |
| FR-5: Architecture test rule + ArticleAllowlist | Task 7 (incorporates arch-review §Amendments 1 and 3) |
| FR-6: No behavioral regression in style guide retrieval | Task 6.3 (exception handling preserved verbatim), Task 6.4 (existing test suite passes), Task 8.1 (full suite) |
| NFR-1 Performance | One virtual call adapter, no new allocations beyond the scoped instance — design unchanged from Leaflet precedent |
| NFR-2 Security | No new secrets/config/network surface — adapter forwards arguments verbatim |
| NFR-3 Maintainability | Architecture rule is the long-term guarantor (Task 7) |
| NFR-4 Backwards compatibility | `IOneDriveService` unchanged (only `GatherContextStep` consumer is swapped) — Tasks 6 and 8.5 verify |

Arch-review amendments are all addressed:
- Amendment 1 (allowlist for `SearchDocuments` types) → Task 7.1
- Amendment 2 (adapter name `KnowledgeBaseArticleStyleGuideSource`) → Task 3.1
- Amendment 3 (`ArticleAllowlist` constant with comments) → Task 7.1
- Amendment 4 (clarify that `KnowledgeBase.UseCases.SearchDocuments` using stays) → Task 6.1
- Amendment 5 (follow-up tracking) → Comment inside `ArticleAllowlist` in Task 7.1 explicitly names the follow-up as a tracking item

**Type consistency check** — verified:
- Interface name `IArticleStyleGuideSource` and method `DownloadStyleGuideTextAsync(string driveId, string path, CancellationToken cancellationToken)` are identical across Task 1 (definition), Task 2 (test), Task 3 (adapter), Task 5 (test mock), Task 6 (call site), Task 7 (allowlist not affected — it tracks `SearchDocuments` types, not the new contract).
- Adapter type `KnowledgeBaseArticleStyleGuideSource` is identical across Tasks 2, 3, 4.
- Field name `_styleGuideSource` is identical across Tasks 5 (mock) and 6 (production).
- The kept-using `Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments` and the removed-using `Anela.Heblo.Application.Features.KnowledgeBase.Services` are spelled consistently in Tasks 5 and 6.

**Placeholder scan** — no `TBD`, no "implement later", no "similar to Task N", no orphan type references. Every code-changing step contains the exact code to write or replace.
