# RAG Knowledge Base – Phase 5: API Surface

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Expose the RAG knowledge base via a REST controller and two MCP tools.

**Architecture:** Two tasks. Task 1 creates `KnowledgeBaseController` with three endpoints. Task 2 creates `KnowledgeBaseTools` MCP class with tests and registers it in `McpModule`.

**Key facts about the codebase:**
- Controllers extend `BaseApiController` and use `HandleResponse(response)` for `BaseResponse` results
- `Route("api/[controller]")` convention → `/api/knowledge-base` for a class named `KnowledgeBaseController`
- MCP tools use `[McpServerToolType]` (class) + `[McpServerTool]` (methods), return `Task<string>` (JSON), throw `McpException` on error
- `ModelContextProtocol` namespace has `McpException`; `ModelContextProtocol.Server` has `McpServerToolType` / `McpServerTool`
- Registration in `McpModule.cs` via `.WithTools<ClassName>()`
- **Existing use case types** (already implemented in Phase 3):
  - `SearchDocumentsRequest { Query, TopK }` → `SearchDocumentsResponse : BaseResponse { List<ChunkResult> Chunks }`
  - `AskQuestionRequest { Question, TopK }` → `AskQuestionResponse : BaseResponse { Answer, List<SourceReference> Sources }`
  - `ChunkResult { ChunkId, DocumentId, Content, Score, SourceFilename, SourcePath }`
  - `SourceReference { DocumentId, Filename, Excerpt, Score }`

**Master plan reference:** `docs/plans/2026-03-02-rag-knowledge-base.md`, Tasks 19–20

---

## Task 1: KnowledgeBaseController

**Files:**
- Create: `backend/src/Anela.Heblo.API/Controllers/KnowledgeBaseController.cs`

**Step 1: Create controller**

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.AskQuestion;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Anela.Heblo.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class KnowledgeBaseController : BaseApiController
{
    private readonly IMediator _mediator;
    private readonly IKnowledgeBaseRepository _repository;

    public KnowledgeBaseController(IMediator mediator, IKnowledgeBaseRepository repository)
    {
        _mediator = mediator;
        _repository = repository;
    }

    [HttpGet("documents")]
    public async Task<IActionResult> GetDocuments(CancellationToken ct)
    {
        var docs = await _repository.GetAllDocumentsAsync(ct);
        return Ok(docs.Select(d => new
        {
            d.Id,
            d.Filename,
            d.Status,
            d.ContentType,
            d.CreatedAt,
            d.IndexedAt
        }));
    }

    [HttpPost("search")]
    public async Task<ActionResult<SearchDocumentsResponse>> Search(
        [FromBody] SearchDocumentsRequest request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(request, ct);
        return HandleResponse(result);
    }

    [HttpPost("ask")]
    public async Task<ActionResult<AskQuestionResponse>> Ask(
        [FromBody] AskQuestionRequest request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(request, ct);
        return HandleResponse(result);
    }
}
```

**Step 2: Build**

```bash
cd backend && dotnet build
```

Expected: builds with no errors.

**Step 3: Format and commit**

```bash
cd backend && dotnet format
git add backend/src/Anela.Heblo.API/Controllers/KnowledgeBaseController.cs
git commit -m "feat: add KnowledgeBaseController with /documents, /search, /ask endpoints"
```

---

## Task 2: KnowledgeBaseTools (MCP) with tests

**Files:**
- Create: `backend/src/Anela.Heblo.API/MCP/Tools/KnowledgeBaseTools.cs`
- Create: `backend/test/Anela.Heblo.Tests/MCP/Tools/KnowledgeBaseToolsTests.cs`
- Modify: `backend/src/Anela.Heblo.API/MCP/McpModule.cs`

**Step 1: Write failing test**

```csharp
using System.Text.Json;
using Anela.Heblo.API.MCP.Tools;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.AskQuestion;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;
using MediatR;
using ModelContextProtocol;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.MCP.Tools;

public class KnowledgeBaseToolsTests
{
    private readonly Mock<IMediator> _mediator = new();

    [Fact]
    public async Task SearchKnowledgeBase_ShouldMapParametersCorrectly()
    {
        var expected = new SearchDocumentsResponse
        {
            Chunks = [new ChunkResult { Content = "Test chunk", Score = 0.9, SourceFilename = "doc.pdf" }]
        };

        _mediator
            .Setup(m => m.Send(
                It.Is<SearchDocumentsRequest>(r => r.Query == "phenoxyethanol" && r.TopK == 3),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var tools = new KnowledgeBaseTools(_mediator.Object);
        var result = await tools.SearchKnowledgeBase("phenoxyethanol", 3);

        var deserialized = JsonSerializer.Deserialize<SearchDocumentsResponse>(result);
        Assert.NotNull(deserialized);
        Assert.Single(deserialized!.Chunks);
        Assert.Equal("Test chunk", deserialized.Chunks[0].Content);
    }

    [Fact]
    public async Task AskKnowledgeBase_ShouldMapParametersCorrectly()
    {
        var expected = new AskQuestionResponse
        {
            Answer = "The max is 1.0%.",
            Sources = [new SourceReference { Filename = "EU_reg.pdf", Score = 0.95 }]
        };

        _mediator
            .Setup(m => m.Send(
                It.Is<AskQuestionRequest>(r => r.Question == "Max phenoxyethanol?"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var tools = new KnowledgeBaseTools(_mediator.Object);
        var result = await tools.AskKnowledgeBase("Max phenoxyethanol?");

        var deserialized = JsonSerializer.Deserialize<AskQuestionResponse>(result);
        Assert.NotNull(deserialized);
        Assert.Equal("The max is 1.0%.", deserialized!.Answer);
        Assert.Single(deserialized.Sources);
    }

    [Fact]
    public async Task SearchKnowledgeBase_ShouldThrowMcpException_WhenMediatorThrows()
    {
        _mediator
            .Setup(m => m.Send(It.IsAny<SearchDocumentsRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("DB error"));

        var tools = new KnowledgeBaseTools(_mediator.Object);

        await Assert.ThrowsAsync<McpException>(() =>
            tools.SearchKnowledgeBase("query"));
    }
}
```

**Step 2: Run test to verify it fails**

```bash
cd backend && dotnet test --filter "KnowledgeBaseToolsTests" -v
```

Expected: FAIL (type not found yet).

**Step 3: Create KnowledgeBaseTools**

```csharp
using System.ComponentModel;
using System.Text.Json;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.AskQuestion;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;
using MediatR;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Anela.Heblo.API.MCP.Tools;

[McpServerToolType]
public class KnowledgeBaseTools
{
    private readonly IMediator _mediator;

    public KnowledgeBaseTools(IMediator mediator)
    {
        _mediator = mediator;
    }

    [McpServerTool]
    [Description("Search the knowledge base for relevant document chunks using semantic similarity. Returns raw chunks with source references.")]
    public async Task<string> SearchKnowledgeBase(
        [Description("Natural language search query")] string query,
        [Description("Number of chunks to return (default: 5)")] int topK = 5)
    {
        try
        {
            var result = await _mediator.Send(new SearchDocumentsRequest
            {
                Query = query,
                TopK = topK
            });
            return JsonSerializer.Serialize(result);
        }
        catch (Exception ex)
        {
            throw new McpException($"Failed to search knowledge base: {ex.Message}");
        }
    }

    [McpServerTool]
    [Description("Ask a question and get an AI-generated answer grounded in company documents. Returns a prose answer with cited sources.")]
    public async Task<string> AskKnowledgeBase(
        [Description("Question to answer using the knowledge base")] string question,
        [Description("Number of context chunks to retrieve (default: 5)")] int topK = 5)
    {
        try
        {
            var result = await _mediator.Send(new AskQuestionRequest
            {
                Question = question,
                TopK = topK
            });
            return JsonSerializer.Serialize(result);
        }
        catch (Exception ex)
        {
            throw new McpException($"Failed to answer question: {ex.Message}");
        }
    }
}
```

**Step 4: Register in McpModule**

Open `backend/src/Anela.Heblo.API/MCP/McpModule.cs`. Add `.WithTools<KnowledgeBaseTools>()`:

```csharp
services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<CatalogMcpTools>()
    .WithTools<ManufactureOrderMcpTools>()
    .WithTools<ManufactureBatchMcpTools>()
    .WithTools<KnowledgeBaseTools>();
```

**Step 5: Run targeted tests to verify they pass**

```bash
cd backend && dotnet test --filter "KnowledgeBaseToolsTests" -v
```

Expected: All 3 tests PASS.

**Step 6: Run full test suite**

```bash
cd backend && dotnet test
```

Expected: All tests pass.

**Step 7: Format and commit**

```bash
cd backend && dotnet format
git add backend/src/Anela.Heblo.API/MCP/Tools/KnowledgeBaseTools.cs
git add backend/src/Anela.Heblo.API/MCP/McpModule.cs
git add backend/test/Anela.Heblo.Tests/MCP/Tools/KnowledgeBaseToolsTests.cs
git commit -m "feat: add KnowledgeBaseTools MCP tools with tests and register in McpModule"
```

---

## Phase 5 complete

REST API and MCP tools are wired up. Next: Phase 6 — final format, build validation, and CLAUDE.md documentation update.
See `docs/plans/2026-03-02-rag-knowledge-base.md`, Task 21.
