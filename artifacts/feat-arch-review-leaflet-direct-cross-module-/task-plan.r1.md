# Decouple Leaflet Module from KnowledgeBase Domain — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace Leaflet's direct dependency on `IKnowledgeBaseRepository` and the `DocumentType` enum (KnowledgeBase Domain) with a Leaflet-owned interface implemented by a KnowledgeBase-owned adapter, and relocate the enum to a Domain shared namespace — preserving runtime behavior, DB schema, and public API.

**Architecture:** Two-part refactor.

1. **Dependency inversion** — Leaflet declares `ILeafletKnowledgeSource` (its own narrow contract for vector search) in `Application/Features/Leaflet/Contracts/`. KnowledgeBase implements it via `KnowledgeBaseLeafletSourceAdapter` in `Application/Features/KnowledgeBase/Infrastructure/` and registers the binding in `KnowledgeBaseModule`. Leaflet handler no longer references any KnowledgeBase type.
2. **Shared enum relocation** — Move `DocumentType` from `Domain/Features/KnowledgeBase/` to `Domain/Shared/Rag/`. Domain entities (`KnowledgeBaseDocument`, `KnowledgeBaseChunk`), shared infra (`OneDriveFolderMapping`), Application handlers, EF configurations, and `LeafletIngestionJob` all import the enum from the new location. Underlying integer values are preserved exactly — no migration, no schema change.

A new reflection-based xUnit boundary test enforces the rule statically so future PRs cannot regress.

**Tech Stack:** .NET 8, xUnit, FluentAssertions, Moq, MediatR, EF Core, `System.Reflection`. No new NuGet packages.

---

## File Structure

### New files
| Path | Responsibility |
|---|---|
| `backend/src/Anela.Heblo.Domain/Shared/Rag/DocumentType.cs` | Cross-module RAG document classification enum. Single source of truth. |
| `backend/src/Anela.Heblo.Application/Features/Leaflet/Contracts/ILeafletKnowledgeSource.cs` | Leaflet-owned read-only abstraction over the knowledge base vector index. |
| `backend/src/Anela.Heblo.Application/Features/Leaflet/Contracts/KnowledgeSearchResult.cs` | Leaflet-owned DTO carrying just `Content` and `Score` — the only fields the handler reads. |
| `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/KnowledgeBaseLeafletSourceAdapter.cs` | Adapter implementing `ILeafletKnowledgeSource` by delegating to `IKnowledgeBaseRepository` and projecting chunks to the Leaflet DTO. |
| `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` | Reflection test asserting no Leaflet type references any KnowledgeBase-owned namespace, with an allowlist for documented pre-existing exceptions. |
| `backend/test/Anela.Heblo.Tests/Features/KnowledgeBase/Infrastructure/KnowledgeBaseLeafletSourceAdapterTests.cs` | Unit tests for the adapter projection. |

### Files modified — type relocation only (using-directive changes)
The enum body moves out of `KnowledgeBaseDocument.cs`. Every consumer of the type must update its `using` directive. Two categories:

**Drop existing `using Anela.Heblo.Domain.Features.KnowledgeBase;`, add `using Anela.Heblo.Domain.Shared.Rag;`** (these files reference *only* `DocumentType` from the KB domain namespace):
- `backend/src/Anela.Heblo.Application/Shared/Rag/OneDriveFolderMapping.cs`
- `backend/src/Anela.Heblo.Application/Features/Leaflet/Infrastructure/Jobs/LeafletIngestionJob.cs`

**Keep existing `using Anela.Heblo.Domain.Features.KnowledgeBase;` AND add `using Anela.Heblo.Domain.Shared.Rag;`** (these files reference `DocumentType` *and* other KB domain types — entities, repository interface, etc.):
- `backend/src/Anela.Heblo.Domain/Features/KnowledgeBase/KnowledgeBaseDocument.cs` (the enum body is removed here; the file keeps `KnowledgeBaseDocument` + `DocumentStatus`)
- `backend/src/Anela.Heblo.Domain/Features/KnowledgeBase/KnowledgeBaseChunk.cs`
- `backend/src/Anela.Heblo.Persistence/KnowledgeBase/KnowledgeBaseChunkConfiguration.cs`
- `backend/src/Anela.Heblo.Persistence/KnowledgeBase/KnowledgeBaseDocumentConfiguration.cs`
- `backend/src/Anela.Heblo.Persistence/KnowledgeBase/KnowledgeBaseRepository.cs`
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/IIndexingStrategy.cs`
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/KnowledgeBaseDocIndexingStrategy.cs`
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/ConversationIndexingStrategy.cs`
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/DocumentIndexingService.cs`
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/Jobs/KnowledgeBaseIngestionJob.cs`
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/IndexDocument/IndexDocumentRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/IndexDocument/IndexDocumentHandler.cs`
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/GetChunkDetail/GetChunkDetailRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/GetChunkDetail/GetChunkDetailHandler.cs`
- `backend/src/Anela.Heblo.API/Controllers/KnowledgeBaseController.cs`

**Note on EF migrations:** Files in `backend/src/Anela.Heblo.Persistence/Migrations/` reference `"DocumentType"` only as a string column name (`b.Property<int>("DocumentType")`). They do **not** import the enum type and require **no** changes.

### Files modified — behavior change
| Path | Change |
|---|---|
| `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/GenerateLeafletHandler.cs` | Replace `IKnowledgeBaseRepository _kb` with `ILeafletKnowledgeSource _kb`. Drop `using Anela.Heblo.Domain.Features.KnowledgeBase;`. Change `h.Chunk.Content` to `h.Content` at line 93. |
| `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs` | Register `ILeafletKnowledgeSource → KnowledgeBaseLeafletSourceAdapter`. |
| `backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/GenerateLeafletHandlerTests.cs` | Swap `Mock<IKnowledgeBaseRepository>` to `Mock<ILeafletKnowledgeSource>`. Replace `(KnowledgeBaseChunk, double)` tuples with `KnowledgeSearchResult` objects. |

### Files modified — documentation
| Path | Change |
|---|---|
| `docs/architecture/development_guidelines.md` | Add a "Consumer-owned contract, provider-owned adapter, provider-owned DI" example using `ILeafletKnowledgeSource`. |
| `docs/architecture/filesystem.md` | Document `Domain/Shared/Rag/` and `Application/Shared/Rag/` as canonical homes for cross-module RAG types. |

---

## Task 1: Relocate `DocumentType` to `Domain/Shared/Rag/`

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Shared/Rag/DocumentType.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/KnowledgeBase/KnowledgeBaseDocument.cs` (remove enum body)
- Modify: 17 source files that import `DocumentType` (full list in File Structure above)

- [ ] **Step 1.1: Create the new enum file**

Create `backend/src/Anela.Heblo.Domain/Shared/Rag/DocumentType.cs`:

```csharp
namespace Anela.Heblo.Domain.Shared.Rag;

public enum DocumentType
{
    KnowledgeBase = 0,
    Conversation = 1,
    Leaflet = 2,
    Article = 3
}
```

Underlying integer values must match the current values exactly (KnowledgeBase=0, Conversation=1, Leaflet=2, Article=3) so the existing `HasConversion<int>()` mapping on the EF columns stays compatible without a migration.

- [ ] **Step 1.2: Remove the enum body from `KnowledgeBaseDocument.cs`**

Edit `backend/src/Anela.Heblo.Domain/Features/KnowledgeBase/KnowledgeBaseDocument.cs`. Change from:

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
    public DocumentType DocumentType { get; set; } = DocumentType.KnowledgeBase;
    public DateTime CreatedAt { get; set; }
    public DateTime? IndexedAt { get; set; }
    public string? DriveId { get; set; }
    public string? GraphItemId { get; set; }

    public ICollection<KnowledgeBaseChunk> Chunks { get; set; } = new List<KnowledgeBaseChunk>();
}

public enum DocumentStatus
{
    Processing,
    Indexed,
    Failed
}

public enum DocumentType
{
    KnowledgeBase = 0,
    Conversation = 1,
    Leaflet = 2,
    Article = 3
}
```

…to:

```csharp
using Anela.Heblo.Domain.Shared.Rag;

namespace Anela.Heblo.Domain.Features.KnowledgeBase;

public class KnowledgeBaseDocument
{
    public Guid Id { get; set; }
    public string Filename { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty; // SHA-256 hex, 64 chars
    public DocumentStatus Status { get; set; } = DocumentStatus.Processing;
    public DocumentType DocumentType { get; set; } = DocumentType.KnowledgeBase;
    public DateTime CreatedAt { get; set; }
    public DateTime? IndexedAt { get; set; }
    public string? DriveId { get; set; }
    public string? GraphItemId { get; set; }

    public ICollection<KnowledgeBaseChunk> Chunks { get; set; } = new List<KnowledgeBaseChunk>();
}

public enum DocumentStatus
{
    Processing,
    Indexed,
    Failed
}
```

`DocumentStatus` stays where it is (KnowledgeBase-local). Only `DocumentType` moves.

- [ ] **Step 1.3: Update `KnowledgeBaseChunk.cs` to import the new namespace**

Edit `backend/src/Anela.Heblo.Domain/Features/KnowledgeBase/KnowledgeBaseChunk.cs`. Add `using Anela.Heblo.Domain.Shared.Rag;` at the top:

```csharp
using Anela.Heblo.Domain.Shared.Rag;

namespace Anela.Heblo.Domain.Features.KnowledgeBase;

public class KnowledgeBaseChunk
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public int ChunkIndex { get; set; }
    public string Content { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public DocumentType DocumentType { get; set; } = DocumentType.KnowledgeBase;
    public float[] Embedding { get; set; } = Array.Empty<float>();

    public KnowledgeBaseDocument Document { get; set; } = null!;
}
```

- [ ] **Step 1.4: Update `OneDriveFolderMapping.cs` — replace using**

Edit `backend/src/Anela.Heblo.Application/Shared/Rag/OneDriveFolderMapping.cs`. Change line 1 from `using Anela.Heblo.Domain.Features.KnowledgeBase;` to `using Anela.Heblo.Domain.Shared.Rag;`. Rest of the file is unchanged.

Resulting file:

```csharp
using Anela.Heblo.Domain.Shared.Rag;

namespace Anela.Heblo.Application.Shared.Rag;

public class OneDriveFolderMapping
{
    public string InboxPath { get; set; } = string.Empty;
    public string ArchivedPath { get; set; } = string.Empty;
    public DocumentType DocumentType { get; set; } = DocumentType.KnowledgeBase;

    /// <summary>
    /// SharePoint drive ID. Find it via Graph API:
    /// GET /v1.0/sites/{hostname}:/sites/{site-name} → get siteId
    /// GET /v1.0/sites/{siteId}/drives → find drive by name, copy "id"
    /// </summary>
    public string DriveId { get; set; } = string.Empty;
}
```

- [ ] **Step 1.5: Update `LeafletIngestionJob.cs` — replace using**

Edit `backend/src/Anela.Heblo.Application/Features/Leaflet/Infrastructure/Jobs/LeafletIngestionJob.cs`. Replace line 4 `using Anela.Heblo.Domain.Features.KnowledgeBase;` with `using Anela.Heblo.Domain.Shared.Rag;`. No other change needed in this file.

After edit, the usings block must read:

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Application.Features.Leaflet.UseCases.IndexLeaflet;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Shared.Rag;
using Anela.Heblo.Domain.Features.Leaflet;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
```

- [ ] **Step 1.6: Add `using Anela.Heblo.Domain.Shared.Rag;` to the remaining 14 consumers**

For each file below, add the line `using Anela.Heblo.Domain.Shared.Rag;` to the usings block (alongside the existing `using Anela.Heblo.Domain.Features.KnowledgeBase;` — these files reference both `DocumentType` and KB-owned types so the existing import stays).

Files to edit (one line added per file):

```
backend/src/Anela.Heblo.Persistence/KnowledgeBase/KnowledgeBaseChunkConfiguration.cs
backend/src/Anela.Heblo.Persistence/KnowledgeBase/KnowledgeBaseDocumentConfiguration.cs
backend/src/Anela.Heblo.Persistence/KnowledgeBase/KnowledgeBaseRepository.cs
backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/IIndexingStrategy.cs
backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/KnowledgeBaseDocIndexingStrategy.cs
backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/ConversationIndexingStrategy.cs
backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/DocumentIndexingService.cs
backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/Jobs/KnowledgeBaseIngestionJob.cs
backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentRequest.cs
backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/IndexDocument/IndexDocumentRequest.cs
backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/IndexDocument/IndexDocumentHandler.cs
backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/GetChunkDetail/GetChunkDetailRequest.cs
backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/GetChunkDetail/GetChunkDetailHandler.cs
backend/src/Anela.Heblo.API/Controllers/KnowledgeBaseController.cs
```

Example: `KnowledgeBaseChunkConfiguration.cs` currently starts:
```csharp
using Anela.Heblo.Domain.Features.KnowledgeBase;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
```

After edit:
```csharp
using Anela.Heblo.Domain.Features.KnowledgeBase;
using Anela.Heblo.Domain.Shared.Rag;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
```

Same pattern for the other 13 files.

- [ ] **Step 1.7: Build the solution to catch any missed consumer**

Run from repo root:
```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: **Build succeeded. 0 Error(s)**.

If any error reports `The type or namespace name 'DocumentType' could not be found`, the file is missing the new `using`. Add `using Anela.Heblo.Domain.Shared.Rag;` and rebuild. Repeat until the build is clean.

- [ ] **Step 1.8: Run the full test suite**

```bash
dotnet test backend/Anela.Heblo.sln --nologo
```

Expected: All tests pass. The relocation is namespace-only — no behavior changed, so no tests should fail. If any do, investigate immediately rather than proceeding.

- [ ] **Step 1.9: Verify EF model snapshot has no schema drift**

The relocation must not produce a model change. Confirm by inspecting `backend/src/Anela.Heblo.Persistence/Migrations/ApplicationDbContextModelSnapshot.cs` is unchanged in `git status` (apart from any unrelated edits). Run:

```bash
git status backend/src/Anela.Heblo.Persistence/Migrations/
```

Expected: no files modified under `Migrations/`. The snapshot uses `b.Property<int>("DocumentType")` — a string column name, not an enum reference — so the namespace change does not touch it.

- [ ] **Step 1.10: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Shared/Rag/DocumentType.cs \
        backend/src/Anela.Heblo.Domain/Features/KnowledgeBase/KnowledgeBaseDocument.cs \
        backend/src/Anela.Heblo.Domain/Features/KnowledgeBase/KnowledgeBaseChunk.cs \
        backend/src/Anela.Heblo.Application/Shared/Rag/OneDriveFolderMapping.cs \
        backend/src/Anela.Heblo.Application/Features/Leaflet/Infrastructure/Jobs/LeafletIngestionJob.cs \
        backend/src/Anela.Heblo.Persistence/KnowledgeBase/ \
        backend/src/Anela.Heblo.Application/Features/KnowledgeBase/ \
        backend/src/Anela.Heblo.API/Controllers/KnowledgeBaseController.cs
git commit -m "refactor: relocate DocumentType enum to Domain/Shared/Rag"
```

---

## Task 2: Introduce Leaflet-owned `KnowledgeSearchResult` DTO and `ILeafletKnowledgeSource` interface

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Leaflet/Contracts/KnowledgeSearchResult.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Leaflet/Contracts/ILeafletKnowledgeSource.cs`

These are pure additions — no existing code depends on them yet. The build must remain green after this task.

- [ ] **Step 2.1: Create `KnowledgeSearchResult.cs`**

Create `backend/src/Anela.Heblo.Application/Features/Leaflet/Contracts/KnowledgeSearchResult.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Leaflet.Contracts;

public class KnowledgeSearchResult
{
    public string Content { get; set; } = string.Empty;
    public double Score { get; set; }
}
```

A **class**, not a record — project rule from `CLAUDE.md` ("DTOs are classes, never C# records"). Only the two fields `GenerateLeafletHandler` reads today (`Score` at line 56, `Content` via `h.Chunk.Content` at line 93). Anything more is YAGNI.

- [ ] **Step 2.2: Create `ILeafletKnowledgeSource.cs`**

Create `backend/src/Anela.Heblo.Application/Features/Leaflet/Contracts/ILeafletKnowledgeSource.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Leaflet.Contracts;

/// <summary>
/// Leaflet-owned read-only abstraction over the knowledge base vector index.
/// Implemented by the KnowledgeBase module via an adapter.
/// </summary>
public interface ILeafletKnowledgeSource
{
    Task<IReadOnlyList<KnowledgeSearchResult>> SearchSimilarAsync(
        float[] queryEmbedding,
        int topK,
        CancellationToken cancellationToken);
}
```

Parameter list mirrors `IKnowledgeBaseRepository.SearchSimilarAsync` (verbatim): `float[] queryEmbedding`, `int topK`, `CancellationToken`. Return type is `IReadOnlyList<KnowledgeSearchResult>` — Leaflet-owned, KB-free.

- [ ] **Step 2.3: Build to verify no syntax errors**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: **Build succeeded. 0 Error(s)**.

- [ ] **Step 2.4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Leaflet/Contracts/
git commit -m "feat: add ILeafletKnowledgeSource contract and KnowledgeSearchResult DTO"
```

---

## Task 3: Add adapter tests (TDD — RED)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/KnowledgeBase/Infrastructure/KnowledgeBaseLeafletSourceAdapterTests.cs`

- [ ] **Step 3.1: Write the failing tests**

Create `backend/test/Anela.Heblo.Tests/Features/KnowledgeBase/Infrastructure/KnowledgeBaseLeafletSourceAdapterTests.cs`:

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase.Infrastructure;
using Anela.Heblo.Application.Features.Leaflet.Contracts;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.KnowledgeBase.Infrastructure;

public class KnowledgeBaseLeafletSourceAdapterTests
{
    private readonly Mock<IKnowledgeBaseRepository> _repository = new();

    private KnowledgeBaseLeafletSourceAdapter CreateAdapter() => new(_repository.Object);

    [Fact]
    public async Task SearchSimilarAsync_forwards_query_to_repository()
    {
        var vector = new[] { 0.1f, 0.2f, 0.3f };
        const int topK = 5;
        var ct = CancellationToken.None;

        _repository
            .Setup(r => r.SearchSimilarAsync(vector, topK, ct))
            .ReturnsAsync(new List<(KnowledgeBaseChunk Chunk, double Score)>());

        var adapter = CreateAdapter();

        await adapter.SearchSimilarAsync(vector, topK, ct);

        _repository.Verify(
            r => r.SearchSimilarAsync(vector, topK, ct),
            Times.Once);
    }

    [Fact]
    public async Task SearchSimilarAsync_projects_chunks_to_KnowledgeSearchResult()
    {
        var chunk1 = new KnowledgeBaseChunk { Id = Guid.NewGuid(), Content = "first chunk content" };
        var chunk2 = new KnowledgeBaseChunk { Id = Guid.NewGuid(), Content = "second chunk content" };

        _repository
            .Setup(r => r.SearchSimilarAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(KnowledgeBaseChunk Chunk, double Score)>
            {
                (chunk1, 0.92),
                (chunk2, 0.71),
            });

        var adapter = CreateAdapter();

        var results = await adapter.SearchSimilarAsync(new[] { 0f }, 10, CancellationToken.None);

        results.Should().HaveCount(2);
        results[0].Content.Should().Be("first chunk content");
        results[0].Score.Should().Be(0.92);
        results[1].Content.Should().Be("second chunk content");
        results[1].Score.Should().Be(0.71);
    }

    [Fact]
    public async Task SearchSimilarAsync_returns_empty_list_when_repository_returns_empty()
    {
        _repository
            .Setup(r => r.SearchSimilarAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<(KnowledgeBaseChunk Chunk, double Score)>());

        var adapter = CreateAdapter();

        var results = await adapter.SearchSimilarAsync(new[] { 0f }, 10, CancellationToken.None);

        results.Should().BeEmpty();
    }
}
```

- [ ] **Step 3.2: Run the tests to verify they fail**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~KnowledgeBaseLeafletSourceAdapterTests" --nologo
```

Expected: **Build error** — `KnowledgeBaseLeafletSourceAdapter` does not exist yet. This is the RED state. (xUnit cannot run tests that don't compile; the build failure here is the test failure.)

---

## Task 4: Implement the adapter (TDD — GREEN)

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/KnowledgeBaseLeafletSourceAdapter.cs`

- [ ] **Step 4.1: Write the minimal implementation**

Create `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/KnowledgeBaseLeafletSourceAdapter.cs`:

```csharp
using Anela.Heblo.Application.Features.Leaflet.Contracts;
using Anela.Heblo.Domain.Features.KnowledgeBase;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Infrastructure;

internal sealed class KnowledgeBaseLeafletSourceAdapter : ILeafletKnowledgeSource
{
    private readonly IKnowledgeBaseRepository _repository;

    public KnowledgeBaseLeafletSourceAdapter(IKnowledgeBaseRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<KnowledgeSearchResult>> SearchSimilarAsync(
        float[] queryEmbedding,
        int topK,
        CancellationToken cancellationToken)
    {
        var hits = await _repository.SearchSimilarAsync(queryEmbedding, topK, cancellationToken);
        return hits
            .Select(h => new KnowledgeSearchResult
            {
                Content = h.Chunk.Content,
                Score = h.Score,
            })
            .ToList();
    }
}
```

`internal sealed` because nothing outside the assembly should construct it directly — DI resolves only through the interface. The test project already references the assembly and uses `InternalsVisibleTo` (per `Anela.Heblo.Tests.csproj`), so tests can still see it.

If the test build fails with "internal class 'KnowledgeBaseLeafletSourceAdapter' is inaccessible due to its protection level," check whether `InternalsVisibleTo("Anela.Heblo.Tests")` is declared in `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`. If not, add it to the csproj `<ItemGroup>`:

```xml
<ItemGroup>
  <InternalsVisibleTo Include="Anela.Heblo.Tests" />
</ItemGroup>
```

Verify by grepping first: if `InternalsVisibleTo` is already declared elsewhere (assembly attribute file, csproj), reuse it.

- [ ] **Step 4.2: Run the adapter tests to verify they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~KnowledgeBaseLeafletSourceAdapterTests" --nologo
```

Expected: **3 passed, 0 failed**.

- [ ] **Step 4.3: Run the full test suite**

```bash
dotnet test backend/Anela.Heblo.sln --nologo
```

Expected: all tests pass — nothing else has changed yet.

- [ ] **Step 4.4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/KnowledgeBaseLeafletSourceAdapter.cs \
        backend/test/Anela.Heblo.Tests/Features/KnowledgeBase/Infrastructure/KnowledgeBaseLeafletSourceAdapterTests.cs
git commit -m "feat: add KnowledgeBaseLeafletSourceAdapter implementing ILeafletKnowledgeSource"
```

If you added `InternalsVisibleTo` to the csproj, add that file to this commit too.

---

## Task 5: Register the adapter in DI

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs`

- [ ] **Step 5.1: Add the DI registration**

Edit `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs`. Add a `using` and a `services.AddScoped` line. Final usings block:

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase.Infrastructure;
using Anela.Heblo.Application.Features.KnowledgeBase.Pipeline;
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Microsoft.Identity.Web;
using Anela.Heblo.Application.Features.KnowledgeBase.Services.DocumentExtractors;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.AskQuestion;
using Anela.Heblo.Application.Features.Leaflet.Contracts;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
```

Inside `AddKnowledgeBaseModule`, after `services.AddScoped<IDocumentIndexingService, DocumentIndexingService>();` (around line 31), add a clearly-commented block:

```csharp
// Cross-module contract: KnowledgeBase implements Leaflet's ILeafletKnowledgeSource via adapter.
// DI registration owned by provider (KnowledgeBase), not consumer (Leaflet) — keeps the
// dependency direction inverted properly. Registration order doesn't matter: DI registrations
// complete before any service resolves.
services.AddScoped<ILeafletKnowledgeSource, KnowledgeBaseLeafletSourceAdapter>();
```

- [ ] **Step 5.2: Build to verify**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: **Build succeeded**.

- [ ] **Step 5.3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs
git commit -m "feat: register ILeafletKnowledgeSource in KnowledgeBaseModule"
```

---

## Task 6: Update `GenerateLeafletHandler` to depend on `ILeafletKnowledgeSource` (TDD — RED)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/GenerateLeafletHandlerTests.cs` (test first)
- Modify: `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/GenerateLeafletHandler.cs` (then handler)

This is the central swap. The tests change first to lock down the new contract; then the handler changes to compile.

- [ ] **Step 6.1: Rewrite `GenerateLeafletHandlerTests.cs` to mock `ILeafletKnowledgeSource`**

Edit `backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/GenerateLeafletHandlerTests.cs`. Make these changes:

**Change the usings block** to:

```csharp
using Anela.Heblo.Application.Features.Leaflet;
using Anela.Heblo.Application.Features.Leaflet.Contracts;
using Anela.Heblo.Application.Features.Leaflet.UseCases.GenerateLeaflet;
using Anela.Heblo.Application.Shared.Rag;
using Anela.Heblo.Domain.Features.Leaflet;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
```

(Drop `using Anela.Heblo.Domain.Features.KnowledgeBase;` — tests no longer touch KB types.)

**Change the field type** at line 17:

```csharp
private readonly Mock<ILeafletKnowledgeSource> _kb = new();
```

(Was `Mock<IKnowledgeBaseRepository>`.)

**Remove the `MakeKbChunk` helper** (lines 75-76) — no longer needed.

**Change the `KbHit` helper** to return `KnowledgeSearchResult` instead of a tuple:

```csharp
private static KnowledgeSearchResult KbHit(double score, string content = "kb content") =>
    new() { Content = content, Score = score };
```

**Update every `kbChunks` list type** throughout the file from `List<(KnowledgeBaseChunk Chunk, double Score)>` to `List<KnowledgeSearchResult>`. There are 4 such declarations:

- `Handle_only_leaflet_empty_logs_cold_start_and_continues` (around line 115): `var kbChunks = new List<KnowledgeSearchResult> { KbHit(0.9, "kb chunk 1"), KbHit(0.8, "kb chunk 2"), KbHit(0.7, "kb chunk 3") };`
- `Handle_filters_below_threshold_chunks` (around line 205): same pattern.

**No callback signature changes** are needed for `_kb.Setup(...).Callback<float[], int, CancellationToken>(...)` — the parameter types of `ILeafletKnowledgeSource.SearchSimilarAsync` are identical to those of `IKnowledgeBaseRepository.SearchSimilarAsync` (the call shape is unchanged).

The `_leaflets` mock and its `(LeafletChunk Chunk, double Score)` tuple shape stay exactly as they are — `ILeafletRepository.SearchSimilarAsync` is not touched by this refactor.

After the edits, the test file should compile only once the handler also changes (Step 6.2). Until then, the build will fail in this test file at the line that constructs `GenerateLeafletHandler` (the constructor signature mismatch). That is the expected RED state.

- [ ] **Step 6.2: Run the tests to verify RED**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GenerateLeafletHandlerTests" --nologo
```

Expected: **Build error** at `GenerateLeafletHandlerTests.cs` — the `new GenerateLeafletHandler(_kb.Object, ...)` call passes an `ILeafletKnowledgeSource` to a constructor that still expects `IKnowledgeBaseRepository`. This is RED.

- [ ] **Step 6.3: Update `GenerateLeafletHandler.cs`**

Edit `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/GenerateLeafletHandler.cs`. Three changes:

**A. Drop the using** at line 3: remove `using Anela.Heblo.Domain.Features.KnowledgeBase;`. Add `using Anela.Heblo.Application.Features.Leaflet.Contracts;`.

**B. Change field and constructor parameter type** at lines 14 and 23 from `IKnowledgeBaseRepository` to `ILeafletKnowledgeSource`.

**C. Drop `.Chunk.` from line 93.** Change:

```csharp
var kbContext = string.Join("\n\n---\n\n", kbHits.Select(h => h.Chunk.Content));
```

to:

```csharp
var kbContext = string.Join("\n\n---\n\n", kbHits.Select(h => h.Content));
```

(The new `kbHits` is `List<KnowledgeSearchResult>` — `Content` is a direct property, no `Chunk` wrapper.)

The final handler file:

```csharp
using Anela.Heblo.Application.Features.Leaflet.Contracts;
using Anela.Heblo.Application.Shared.Http;
using Anela.Heblo.Application.Shared.Rag;
using Anela.Heblo.Domain.Features.Leaflet;
using MediatR;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Leaflet.UseCases.GenerateLeaflet;

public class GenerateLeafletHandler : IRequestHandler<GenerateLeafletRequest, GenerateLeafletResponse>
{
    private readonly ILeafletKnowledgeSource _kb;
    private readonly ILeafletRepository _leaflets;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddings;
    private readonly IRagQueryExpander _expander;
    private readonly IChatClient _chat;
    private readonly LeafletOptions _options;
    private readonly ILogger<GenerateLeafletHandler> _logger;

    public GenerateLeafletHandler(
        ILeafletKnowledgeSource kb,
        ILeafletRepository leaflets,
        IEmbeddingGenerator<string, Embedding<float>> embeddings,
        IRagQueryExpander expander,
        IChatClient chat,
        IOptions<LeafletOptions> options,
        ILogger<GenerateLeafletHandler> logger)
    {
        _kb = kb;
        _leaflets = leaflets;
        _embeddings = embeddings;
        _expander = expander;
        _chat = chat;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<GenerateLeafletResponse> Handle(
        GenerateLeafletRequest request,
        CancellationToken cancellationToken)
    {
        var ct = cancellationToken;

        var queryToEmbed = await _expander.ExpandAsync(
            request.Topic, _options.ToExpansionConfig(), ct);

        var topicVector = (await ChatRetry.RetryOnceAsync(
                () => _embeddings.GenerateAsync([queryToEmbed], cancellationToken: ct),
                _logger,
                ct))
            .First().Vector.ToArray();

        var kbHits = (await _kb.SearchSimilarAsync(topicVector, _options.KbTopK, ct))
            .Where(x => x.Score >= _options.MinSimilarityScore)
            .ToList();

        var leafletHits = (await _leaflets.SearchSimilarAsync(topicVector, _options.LeafletTopK, ct))
            .Where(x => x.Score >= _options.MinSimilarityScore)
            .ToList();

        if (kbHits.Count == 0 && leafletHits.Count == 0)
        {
            throw new EmptyRetrievalException(
                "Knowledge Base does not yet cover this topic; try a broader phrasing");
        }

        var coldStart = leafletHits.Count == 0 ? "true" : "false";

        if (leafletHits.Count == 0)
        {
            _logger.LogWarning(
                "Leaflet cold-start: zero leaflet style references for topic '{Topic}'",
                request.Topic);
        }

        var lengthWords = request.Length switch
        {
            LeafletLength.Short => _options.ShortWordTarget,
            LeafletLength.Medium => _options.MediumWordTarget,
            LeafletLength.Long => _options.LongWordTarget,
            _ => throw new ArgumentOutOfRangeException(nameof(request.Length), request.Length, "Unknown leaflet length"),
        };

        var audienceLabel = request.Audience switch
        {
            AudienceType.B2B => "B2B",
            AudienceType.EndConsumer => "Koncový zákazník",
            _ => throw new ArgumentOutOfRangeException(nameof(request.Audience), request.Audience, "Unknown audience type"),
        };

        var kbContext = string.Join("\n\n---\n\n", kbHits.Select(h => h.Content));
        var stage1System = _options.Stage1SystemPrompt
            .Replace("{topic}", request.Topic)
            .Replace("{audience}", audienceLabel)
            .Replace("{length}", lengthWords.ToString())
            .Replace("{kbContext}", string.IsNullOrWhiteSpace(kbContext) ? "(empty)" : kbContext);

        var chatOptions = new ChatOptions { ModelId = _options.ChatModel, MaxOutputTokens = _options.ChatMaxTokens };

        var outlineResponse = await ChatRetry.RetryOnceAsync(
            () => _chat.GetResponseAsync(
                [
                    new ChatMessage(ChatRole.System, stage1System),
                    new ChatMessage(ChatRole.User, request.Topic)
                ],
                chatOptions,
                ct),
            _logger,
            ct);

        var outline = outlineResponse.Text ?? string.Empty;

        var leafletContext = string.Join("\n\n---\n\n", leafletHits.Select(h => h.Chunk.Content));
        var stage2System = _options.Stage2SystemPrompt
            .Replace("{topic}", request.Topic)
            .Replace("{audience}", audienceLabel)
            .Replace("{length}", lengthWords.ToString())
            .Replace("{coldStart}", coldStart)
            .Replace("{leafletContext}", string.IsNullOrWhiteSpace(leafletContext) ? "(none)" : leafletContext);

        var leafletResponse = await ChatRetry.RetryOnceAsync(
            () => _chat.GetResponseAsync(
                [
                    new ChatMessage(ChatRole.System, stage2System),
                    new ChatMessage(ChatRole.User, outline)
                ],
                chatOptions,
                ct),
            _logger,
            ct);

        return new GenerateLeafletResponse
        {
            Content = leafletResponse.Text ?? string.Empty,
            KbSourceCount = kbHits.Count,
            LeafletSourceCount = leafletHits.Count,
        };
    }
}
```

Note `leafletHits.Select(h => h.Chunk.Content)` further down is **unchanged** — `_leaflets` is `ILeafletRepository` (Leaflet-domain), not the new contract. Only the KB-side projection drops `.Chunk`.

- [ ] **Step 6.4: Run the tests to verify GREEN**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GenerateLeafletHandlerTests" --nologo
```

Expected: **All `GenerateLeafletHandlerTests` pass**.

- [ ] **Step 6.5: Run the full test suite**

```bash
dotnet test backend/Anela.Heblo.sln --nologo
```

Expected: all tests pass.

- [ ] **Step 6.6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/GenerateLeafletHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/GenerateLeafletHandlerTests.cs
git commit -m "refactor: GenerateLeafletHandler depends on ILeafletKnowledgeSource"
```

---

## Task 7: Add the architecture boundary test

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`

This test reflects over the `Anela.Heblo.Application` assembly to assert that no Leaflet type references any KnowledgeBase-owned type. The check scope follows the architect's amendment to FR-5: it covers `Anela.Heblo.Domain.Features.KnowledgeBase`, `Anela.Heblo.Application.Features.KnowledgeBase`, and `Anela.Heblo.Persistence.KnowledgeBase`. The allowlist seeds one entry for a pre-existing dependency on `IOneDriveService` in `LeafletIngestionJob.cs` — this is out of scope per the spec and documented as such in the allowlist comment.

- [ ] **Step 7.1: Write the test file**

Create `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`:

```csharp
using System.Reflection;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Architecture;

/// <summary>
/// Enforces module boundary rule from docs/architecture/development_guidelines.md:
/// Leaflet must not reference any KnowledgeBase-owned type directly. All cross-module
/// communication goes through Leaflet-owned contracts (e.g. ILeafletKnowledgeSource)
/// implemented by KnowledgeBase via an adapter.
/// </summary>
public class ModuleBoundariesTests
{
    // Namespaces that, if referenced from a Leaflet type, indicate a boundary violation.
    private static readonly string[] ForbiddenNamespacePrefixes =
    [
        "Anela.Heblo.Domain.Features.KnowledgeBase",
        "Anela.Heblo.Application.Features.KnowledgeBase",
        "Anela.Heblo.Persistence.KnowledgeBase",
    ];

    private const string LeafletNamespacePrefix = "Anela.Heblo.Application.Features.Leaflet";

    // Allowlist for explicitly-documented exceptions. Each entry needs a comment with the
    // justification. Entries should be removed as the underlying violations are fixed.
    //
    // Entry format: "{LeafletFullyQualifiedTypeName} -> {ForbiddenTypeFullName}"
    private static readonly HashSet<string> Allowlist = new(StringComparer.Ordinal)
    {
        // Pre-existing dependency: LeafletIngestionJob consumes IOneDriveService, which currently
        // lives in Anela.Heblo.Application.Features.KnowledgeBase.Services. Lifting this is out of
        // scope for the 2026-05-15 Leaflet decoupling. Track separately and remove this entry
        // when IOneDriveService is relocated to a shared namespace.
        "Anela.Heblo.Application.Features.Leaflet.Infrastructure.Jobs.LeafletIngestionJob -> Anela.Heblo.Application.Features.KnowledgeBase.Services.IOneDriveService",
        "Anela.Heblo.Application.Features.Leaflet.Infrastructure.Jobs.LeafletIngestionJob -> Anela.Heblo.Application.Features.KnowledgeBase.Services.OneDriveFile",
    };

    [Fact]
    public void Leaflet_types_should_not_reference_KnowledgeBase_owned_namespaces()
    {
        var assembly = Assembly.Load("Anela.Heblo.Application");
        var leafletTypes = assembly.GetTypes()
            .Where(t => t.Namespace is not null && t.Namespace.StartsWith(LeafletNamespacePrefix, StringComparison.Ordinal))
            .ToList();

        var violations = new List<string>();

        foreach (var leafletType in leafletTypes)
        {
            foreach (var (referencedType, memberDescription) in EnumerateReferencedTypes(leafletType))
            {
                if (!IsForbidden(referencedType))
                    continue;

                var entry = $"{leafletType.FullName} -> {referencedType.FullName}";
                if (Allowlist.Contains(entry))
                    continue;

                violations.Add($"{entry} (via {memberDescription})");
            }
        }

        violations.Should().BeEmpty(
            "Leaflet types must not reference KnowledgeBase-owned namespaces. " +
            "Define a Leaflet-owned contract in Application/Features/Leaflet/Contracts/ " +
            "and have KnowledgeBase implement it via an adapter. " +
            "Found:\n  " + string.Join("\n  ", violations));
    }

    private static bool IsForbidden(Type type)
    {
        if (type.Namespace is null)
            return false;

        foreach (var prefix in ForbiddenNamespacePrefixes)
        {
            if (type.Namespace.Equals(prefix, StringComparison.Ordinal) ||
                type.Namespace.StartsWith(prefix + ".", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Enumerates every type referenced by a given type: constructor parameters, fields,
    /// properties, method parameters, method return types, generic type arguments,
    /// and attribute types. Returns (referencedType, "where it appeared") tuples.
    ///
    /// Known limitation: does not inspect method bodies (local variable types,
    /// inlined call targets). Generic constraints and attribute constructor args
    /// are covered partially via Type/CustomAttribute traversal.
    /// </summary>
    private static IEnumerable<(Type Type, string Where)> EnumerateReferencedTypes(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                    BindingFlags.Instance | BindingFlags.Static |
                                    BindingFlags.DeclaredOnly;

        foreach (var attr in type.GetCustomAttributesData())
            foreach (var t in ExpandGenerics(attr.AttributeType))
                yield return (t, $"attribute [{attr.AttributeType.Name}]");

        foreach (var field in type.GetFields(flags))
            foreach (var t in ExpandGenerics(field.FieldType))
                yield return (t, $"field {field.Name}");

        foreach (var prop in type.GetProperties(flags))
            foreach (var t in ExpandGenerics(prop.PropertyType))
                yield return (t, $"property {prop.Name}");

        foreach (var ctor in type.GetConstructors(flags))
            foreach (var param in ctor.GetParameters())
                foreach (var t in ExpandGenerics(param.ParameterType))
                    yield return (t, $"ctor parameter {param.Name}");

        foreach (var method in type.GetMethods(flags))
        {
            foreach (var t in ExpandGenerics(method.ReturnType))
                yield return (t, $"method {method.Name} return");

            foreach (var param in method.GetParameters())
                foreach (var t in ExpandGenerics(param.ParameterType))
                    yield return (t, $"method {method.Name} parameter {param.Name}");
        }
    }

    private static IEnumerable<Type> ExpandGenerics(Type type)
    {
        if (type.IsByRef || type.IsPointer)
            type = type.GetElementType() ?? type;

        if (type.IsArray)
        {
            var elem = type.GetElementType();
            if (elem is not null)
                foreach (var t in ExpandGenerics(elem))
                    yield return t;
            yield break;
        }

        yield return type;

        if (type.IsGenericType)
        {
            foreach (var arg in type.GetGenericArguments())
                foreach (var t in ExpandGenerics(arg))
                    yield return t;
        }
    }
}
```

- [ ] **Step 7.2: Run the test to verify it passes on current state**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ModuleBoundariesTests" --nologo
```

Expected: **1 passed, 0 failed**. The Leaflet handler no longer references KB; `LeafletIngestionJob`'s `IOneDriveService` + `OneDriveFile` references are on the allowlist.

- [ ] **Step 7.3: Sanity-check the test by introducing a temporary violation**

The test must actually catch regressions. To verify:

1. Edit `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/GenerateLeafletHandler.cs`. Temporarily add `using Anela.Heblo.Domain.Features.KnowledgeBase;` back at the top, then add an unused field:

   ```csharp
   private readonly IKnowledgeBaseRepository? _temporaryViolation = null;
   ```

2. Rebuild and run the boundary test:

   ```bash
   dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
     --filter "FullyQualifiedName~ModuleBoundariesTests" --nologo
   ```

   Expected: **FAIL** — the failure message names `GenerateLeafletHandler -> IKnowledgeBaseRepository (via field _temporaryViolation)`.

3. Revert both edits to restore the handler exactly as left at the end of Task 6. Rebuild and rerun the boundary test:

   ```bash
   dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
     --filter "FullyQualifiedName~ModuleBoundariesTests" --nologo
   ```

   Expected: **1 passed**.

This step proves the test is not vacuously passing.

- [ ] **Step 7.4: Run the full test suite**

```bash
dotnet test backend/Anela.Heblo.sln --nologo
```

Expected: all tests pass.

- [ ] **Step 7.5: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
git commit -m "test: enforce Leaflet → KnowledgeBase boundary via reflection test"
```

---

## Task 8: Documentation

**Files:**
- Modify: `docs/architecture/development_guidelines.md`
- Modify: `docs/architecture/filesystem.md`

- [ ] **Step 8.1: Add cross-module communication example to `development_guidelines.md`**

Edit `docs/architecture/development_guidelines.md`. After the "Module Communication" subsection (around line 188-192, under "🚀 Development Workflow"), append a worked example:

```markdown
### Cross-Module Communication Example: ILeafletKnowledgeSource

When module A needs read-only access to data in module B, the dependency must invert:

1. **Consumer (A) defines the contract.** Module A declares an interface in its own `Contracts/` folder, exposing only the operations it actually consumes (no speculative methods).
2. **Provider (B) implements the contract via an adapter.** Module B writes an adapter class that delegates to its existing internal services. The adapter lives in module B's `Infrastructure/`.
3. **Provider (B) registers the DI binding.** Module B's `{Module}.cs` registers `services.AddScoped<IConsumerContract, ProviderAdapter>();`. The consumer module never touches this registration.

Concrete example in this codebase:

- Consumer: `Anela.Heblo.Application.Features.Leaflet.Contracts.ILeafletKnowledgeSource` (Leaflet-owned)
- Provider: `Anela.Heblo.Application.Features.KnowledgeBase.Infrastructure.KnowledgeBaseLeafletSourceAdapter` (KnowledgeBase-owned)
- DI: `KnowledgeBaseModule.AddKnowledgeBaseModule` registers the binding

A reflection-based test in `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` enforces that Leaflet types contain no references to KnowledgeBase-owned namespaces. Future regressions fail CI.
```

- [ ] **Step 8.2: Document `Domain/Shared/Rag/` and `Application/Shared/Rag/` in `filesystem.md`**

Edit `docs/architecture/filesystem.md`. In the Domain Layer subsection (around line 140-145), after the "For complex domains, use subfolders" bullet, append:

```markdown
- **Shared/**: Cross-cutting domain utilities (e.g., `CurrencyCode`, `Result`)
  - **Shared/Rag/**: Canonical home for cross-module RAG **domain** types — entities, value objects, and enums that span multiple feature modules and must live in Domain to satisfy Clean Architecture layering. Example: `DocumentType` (shared between KnowledgeBase, Leaflet, Conversation, and Article modules).
```

In the Application Layer subsection (around line 147-156), after the "Features/{Feature}/{Feature}Module.cs" bullet, append:

```markdown
- **Shared/Rag/**: Cross-module RAG **application/infrastructure** types — options base classes, helpers, shared services (`RagFeatureOptions`, `OneDriveFolderMapping`, `IRagQueryExpander`). Distinct from `Domain/Shared/Rag/`, which holds Domain-layer RAG types.
```

- [ ] **Step 8.3: Commit**

```bash
git add docs/architecture/development_guidelines.md docs/architecture/filesystem.md
git commit -m "docs: document cross-module contract pattern and shared RAG namespaces"
```

---

## Task 9: Final verification

- [ ] **Step 9.1: Full build**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: **Build succeeded. 0 Error(s). 0 Warning(s)** (or matching the baseline warning count).

- [ ] **Step 9.2: Format check**

```bash
dotnet format backend/Anela.Heblo.sln --verify-no-changes
```

Expected: exit code 0. If non-zero, run `dotnet format backend/Anela.Heblo.sln` to apply fixes, review the diff, and commit.

- [ ] **Step 9.3: Full test suite**

```bash
dotnet test backend/Anela.Heblo.sln --nologo
```

Expected: **all tests passed**.

- [ ] **Step 9.4: Confirm no unintended migration**

```bash
git status backend/src/Anela.Heblo.Persistence/Migrations/
```

Expected: clean. The relocation preserved the enum's underlying integer values, so the EF model snapshot is unchanged.

- [ ] **Step 9.5: Confirm Leaflet no longer references KnowledgeBase Domain**

```bash
grep -r "Anela.Heblo.Domain.Features.KnowledgeBase" backend/src/Anela.Heblo.Application/Features/Leaflet/
```

Expected: **no matches**.

- [ ] **Step 9.6: Confirm the new contract is in place**

```bash
ls backend/src/Anela.Heblo.Application/Features/Leaflet/Contracts/
ls backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/
ls backend/src/Anela.Heblo.Domain/Shared/Rag/
```

Expected output:
- `Contracts/` — `ILeafletKnowledgeSource.cs`, `KnowledgeSearchResult.cs`
- `Infrastructure/` — `KnowledgeBaseLeafletSourceAdapter.cs` (plus any pre-existing files like `Jobs/`)
- `Domain/Shared/Rag/` — `DocumentType.cs`

---

## Self-Review Summary

**Spec coverage:**
- FR-1 (`ILeafletKnowledgeSource`): Task 2.2
- FR-2 (adapter): Tasks 3–4
- FR-3 (handler swap): Task 6
- FR-4 (`DocumentType` relocation, per architect's amendment to Domain/Shared/Rag): Task 1
- FR-5 (boundary test with broadened scope per architect's amendment): Task 7
- FR-6 (docs): Task 8
- NFR-1 (performance): Adapter is a single LINQ projection over at most `KbTopK=8` items; no extra async hops. Verified by inspection.
- NFR-2 (security): No auth/data-sensitivity changes.
- NFR-3 (backwards compat): No HTTP API change; `DocumentType` integer values preserved; EF model snapshot unchanged (Step 9.4 verifies).
- NFR-4 (testability): Adapter tests (Task 3) use `IKnowledgeBaseRepository` mock; handler tests (Task 6) mock `ILeafletKnowledgeSource` without any KB dependency.
- NFR-5 (code quality): DTO is a class, files focused, no methods exceed 50 lines.

**Placeholder scan:** No TBDs, no "add appropriate error handling," no missing code blocks.

**Type consistency:** `ILeafletKnowledgeSource.SearchSimilarAsync` signature (`float[]`, `int`, `CancellationToken` → `Task<IReadOnlyList<KnowledgeSearchResult>>`) is identical across Tasks 2, 3, 4, 6. `KnowledgeSearchResult` (class with `Content: string`, `Score: double`) is referenced uniformly.

**Notes:**
- The architect's HIGH-severity risk about `*.Designer.cs` / `ApplicationDbContextModelSnapshot.cs` needing namespace updates is **incorrect**: those files reference `"DocumentType"` only as a string column name with `b.Property<int>(...)`. They do not import the enum type and need no changes. Step 1.9 verifies this empirically.
- A pre-existing dependency exists from `LeafletIngestionJob` on `IOneDriveService` (in `Application.Features.KnowledgeBase.Services`). The broadened boundary test (FR-5 per the architect's amendment) would catch it. The spec explicitly defers similar audits out of scope, so the dependency is added to the boundary test allowlist with a justifying comment (Task 7.1). The allowlist is the spec's intended mechanism for documented exceptions; this exact scenario is the reason it exists.
