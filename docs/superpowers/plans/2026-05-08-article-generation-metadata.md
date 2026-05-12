# Article Generation Metadata Persistence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Persist a per-step metadata trail for every article generation pipeline run so that search queries, LLM responses, gathered snippets, facts, and validation results can be inspected for debugging and validation.

**Architecture:** Add an `ArticleGenerationStep` entity (one row per pipeline step) stored with `text` JSON columns, cascade-deleted from `Article`. A `PipelineStepRecorder` helper wraps each of the five existing pipeline steps to record timing, model, and serialized input/output. A new `GET /api/articles/{id}/trace` endpoint exposes the trail, and a collapsible React Debug panel renders it in `ArticleDetail`.

**Tech Stack:** .NET 8, EF Core / Npgsql PostgreSQL, MediatR, xUnit + FluentAssertions + Moq, React + TanStack Query, Tailwind CSS.

---

## File Map

### New files
| File | Responsibility |
|------|---------------|
| `backend/src/Anela.Heblo.Domain/Features/Article/ArticleGenerationStep.cs` | Domain entity |
| `backend/src/Anela.Heblo.Domain/Features/Article/ArticleGenerationStepStatus.cs` | Status enum |
| `backend/src/Anela.Heblo.Persistence/Features/Article/ArticleGenerationStepConfiguration.cs` | EF Fluent config |
| `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/PipelineStepRecorder.cs` | Timing/persistence helper |
| `backend/src/Anela.Heblo.Application/Features/Article/UseCases/GetArticleTrace/GetArticleTraceRequest.cs` | MediatR request |
| `backend/src/Anela.Heblo.Application/Features/Article/UseCases/GetArticleTrace/GetArticleTraceHandler.cs` | Handler |
| `backend/src/Anela.Heblo.Application/Features/Article/UseCases/GetArticleTrace/GetArticleTraceResponse.cs` | Response DTOs (classes) |
| `backend/test/Anela.Heblo.Tests/Features/Article/PipelineStepRecorderTests.cs` | Unit tests |
| `backend/test/Anela.Heblo.Tests/Features/Article/GetArticleTraceHandlerTests.cs` | Unit tests |
| `frontend/src/api/hooks/useArticleTrace.ts` | React Query hook |
| `frontend/src/features/articles/ArticleDebugPanel.tsx` | Collapsible debug UI |

### Modified files
| File | Change |
|------|--------|
| `backend/src/Anela.Heblo.Domain/Features/Article/Article.cs` | Add `Steps` nav property |
| `backend/src/Anela.Heblo.Domain/Features/Article/IArticleRepository.cs` | Add 3 step methods |
| `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs` | Add `ArticleGenerationSteps` DbSet |
| `backend/src/Anela.Heblo.Persistence/Features/Article/ArticleConfiguration.cs` | Add `HasMany Steps` |
| `backend/src/Anela.Heblo.Persistence/Features/Article/ArticleRepository.cs` | Implement 3 new methods |
| `backend/src/Anela.Heblo.Application/Features/Article/ArticleModule.cs` | Register `PipelineStepRecorder` |
| `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/PlanQueriesStep.cs` | Wrap with recorder |
| `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/GatherContextStep.cs` | Wrap with recorder |
| `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/AggregateFactsStep.cs` | Wrap with recorder |
| `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/ValidateFactsStep.cs` | Wrap with recorder |
| `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/WriteArticleStep.cs` | Wrap with recorder |
| `backend/src/Anela.Heblo.API/Controllers/ArticlesController.cs` | Add trace endpoint |
| `frontend/src/features/articles/ArticleDetail.tsx` | Add debug panel |

---

## Task 1: Domain entity and enum

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Article/ArticleGenerationStepStatus.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/Article/ArticleGenerationStep.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/Article/Article.cs`

- [ ] **Step 1.1: Create the status enum**

```csharp
// backend/src/Anela.Heblo.Domain/Features/Article/ArticleGenerationStepStatus.cs
namespace Anela.Heblo.Domain.Features.Article;

public enum ArticleGenerationStepStatus
{
    Running,
    Succeeded,
    Failed,
}
```

- [ ] **Step 1.2: Create the entity**

```csharp
// backend/src/Anela.Heblo.Domain/Features/Article/ArticleGenerationStep.cs
namespace Anela.Heblo.Domain.Features.Article;

public sealed class ArticleGenerationStep
{
    public Guid Id { get; set; }
    public Guid ArticleId { get; set; }
    public string StepName { get; set; } = "";
    public int Sequence { get; set; }
    public ArticleGenerationStepStatus Status { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public long? DurationMs { get; set; }
    public string? Model { get; set; }
    public string? InputJson { get; set; }
    public string? OutputJson { get; set; }
    public string? ErrorMessage { get; set; }
}
```

- [ ] **Step 1.3: Add navigation property to Article**

In `backend/src/Anela.Heblo.Domain/Features/Article/Article.cs`, add after the existing `Sources` property (line 29):

```csharp
public List<ArticleGenerationStep> Steps { get; set; } = new();
```

- [ ] **Step 1.4: Build to verify**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet build Anela.Heblo.sln --nologo -q
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 1.5: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Article/ArticleGenerationStepStatus.cs \
        backend/src/Anela.Heblo.Domain/Features/Article/ArticleGenerationStep.cs \
        backend/src/Anela.Heblo.Domain/Features/Article/Article.cs
git commit -m "feat(articles): add ArticleGenerationStep domain entity and status enum"
```

---

## Task 2: Repository interface additions

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Article/IArticleRepository.cs`

- [ ] **Step 2.1: Add three new methods to the interface**

Replace the entire content of `IArticleRepository.cs` with:

```csharp
namespace Anela.Heblo.Domain.Features.Article;

public interface IArticleRepository
{
    Task AddAsync(Article article, CancellationToken ct = default);
    Task<Article?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Article?> GetForUpdateAsync(Guid id, CancellationToken ct = default);
    Task<(List<Article> Items, int TotalCount)> GetPagedAsync(ArticleStatus? status, int page, int pageSize, CancellationToken ct = default);
    Task<(IReadOnlyList<Article> Items, int TotalCount)> GetFeedbackPagedAsync(
        bool? hasFeedback,
        string? requestedBy,
        string sortBy,
        bool descending,
        int page,
        int pageSize,
        CancellationToken ct = default);
    Task<ArticleFeedbackStats> GetFeedbackStatsAsync(CancellationToken ct = default);
    Task<Article?> GetWithStepsAsync(Guid id, CancellationToken ct = default);
    Task AddStepAsync(ArticleGenerationStep step, CancellationToken ct = default);
    Task UpdateStepAsync(ArticleGenerationStep step, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
```

- [ ] **Step 2.2: Build to confirm interface compiles**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet build Anela.Heblo.sln --nologo -q
```

Expected: Build failed with "does not implement interface member" errors in `ArticleRepository`. That is expected — we haven't updated the repository yet.

- [ ] **Step 2.3: Commit the interface change**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Article/IArticleRepository.cs
git commit -m "feat(articles): extend IArticleRepository with step persistence methods"
```

---

## Task 3: EF Core persistence

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/Features/Article/ArticleGenerationStepConfiguration.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/Features/Article/ArticleConfiguration.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/Features/Article/ArticleRepository.cs`

- [ ] **Step 3.1: Create EF configuration for ArticleGenerationStep**

```csharp
// backend/src/Anela.Heblo.Persistence/Features/Article/ArticleGenerationStepConfiguration.cs
using Anela.Heblo.Domain.Features.Article;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Features.Article;

public class ArticleGenerationStepConfiguration : IEntityTypeConfiguration<ArticleGenerationStep>
{
    public void Configure(EntityTypeBuilder<ArticleGenerationStep> builder)
    {
        builder.ToTable("ArticleGenerationSteps");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.StepName).IsRequired().HasMaxLength(50);
        builder.Property(x => x.Sequence).IsRequired();
        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(20);
        builder.Property(x => x.StartedAt).IsRequired();
        builder.Property(x => x.FinishedAt).IsRequired(false);
        builder.Property(x => x.DurationMs).IsRequired(false);
        builder.Property(x => x.Model).IsRequired(false).HasMaxLength(100);
        builder.Property(x => x.InputJson).IsRequired(false).HasColumnType("text");
        builder.Property(x => x.OutputJson).IsRequired(false).HasColumnType("text");
        builder.Property(x => x.ErrorMessage).IsRequired(false).HasMaxLength(2000);

        builder.HasIndex(x => new { x.ArticleId, x.Sequence })
            .HasDatabaseName("IX_ArticleGenerationSteps_ArticleId_Sequence");
    }
}
```

- [ ] **Step 3.2: Add HasMany Steps to ArticleConfiguration**

In `backend/src/Anela.Heblo.Persistence/Features/Article/ArticleConfiguration.cs`, after the existing `HasMany(x => x.Sources)` block (after line 43), add:

```csharp
        builder.HasMany(x => x.Steps)
            .WithOne()
            .HasForeignKey(x => x.ArticleId)
            .OnDelete(DeleteBehavior.Cascade);
```

- [ ] **Step 3.3: Add DbSet to ApplicationDbContext**

In `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs`, after line 96 (`public DbSet<ArticleSource> ArticleSources...`), add:

```csharp
    public DbSet<ArticleGenerationStep> ArticleGenerationSteps { get; set; } = null!;
```

Also add the missing using at the top of the file if not already present:
```csharp
using Anela.Heblo.Domain.Features.Article;
```

- [ ] **Step 3.4: Implement three new repository methods**

In `backend/src/Anela.Heblo.Persistence/Features/Article/ArticleRepository.cs`, add these three methods before `SaveChangesAsync`:

```csharp
    public async Task<DomainArticle?> GetWithStepsAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.Articles
            .AsNoTracking()
            .Include(a => a.Steps.OrderBy(s => s.Sequence))
            .FirstOrDefaultAsync(a => a.Id == id, ct);
    }

    public Task AddStepAsync(ArticleGenerationStep step, CancellationToken ct = default)
    {
        _context.ArticleGenerationSteps.Add(step);
        return _context.SaveChangesAsync(ct);
    }

    public Task UpdateStepAsync(ArticleGenerationStep step, CancellationToken ct = default)
    {
        _context.ArticleGenerationSteps.Update(step);
        return _context.SaveChangesAsync(ct);
    }
```

Also add the missing using at the top of `ArticleRepository.cs`:
```csharp
using Anela.Heblo.Domain.Features.Article;
```

- [ ] **Step 3.5: Build to verify no errors**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet build Anela.Heblo.sln --nologo -q
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 3.6: Generate EF migration**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet ef migrations add AddArticleGenerationSteps \
  --project src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj \
  --startup-project src/Anela.Heblo.API/Anela.Heblo.API.csproj \
  --output-dir Migrations
```

Expected: New migration file created under `Migrations/`. Do NOT run `dotnet ef database update` — migrations are applied manually in this project.

- [ ] **Step 3.7: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Features/Article/ArticleGenerationStepConfiguration.cs \
        backend/src/Anela.Heblo.Persistence/Features/Article/ArticleConfiguration.cs \
        backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs \
        backend/src/Anela.Heblo.Persistence/Features/Article/ArticleRepository.cs \
        backend/src/Anela.Heblo.Persistence/Migrations/
git commit -m "feat(articles): add ArticleGenerationSteps table and EF migration"
```

---

## Task 4: PipelineStepRecorder helper and DI registration

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/PipelineStepRecorder.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Article/ArticleModule.cs`

- [ ] **Step 4.1: Write the failing test**

Create `backend/test/Anela.Heblo.Tests/Features/Article/PipelineStepRecorderTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline;
using Anela.Heblo.Domain.Features.Article;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Article;

public class PipelineStepRecorderTests
{
    private readonly Mock<IArticleRepository> _repo = new();
    private readonly Guid _articleId = Guid.NewGuid();

    public PipelineStepRecorderTests()
    {
        _repo.Setup(r => r.AddStepAsync(It.IsAny<ArticleGenerationStep>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);
        _repo.Setup(r => r.UpdateStepAsync(It.IsAny<ArticleGenerationStep>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task RecordAsync_AddsRunningStep_ThenSucceededStep_OnSuccess()
    {
        // Arrange
        var recorder = new PipelineStepRecorder(_repo.Object);
        ArticleGenerationStep? addedStep = null;
        ArticleGenerationStep? updatedStep = null;

        _repo.Setup(r => r.AddStepAsync(It.IsAny<ArticleGenerationStep>(), It.IsAny<CancellationToken>()))
             .Callback<ArticleGenerationStep, CancellationToken>((s, _) => addedStep = s)
             .Returns(Task.CompletedTask);
        _repo.Setup(r => r.UpdateStepAsync(It.IsAny<ArticleGenerationStep>(), It.IsAny<CancellationToken>()))
             .Callback<ArticleGenerationStep, CancellationToken>((s, _) => updatedStep = s)
             .Returns(Task.CompletedTask);

        // Act
        var result = await recorder.RecordAsync<string>(
            _articleId, "PlanQueries", 1, "test-model", new { topic = "test" },
            () => Task.FromResult(("result", (object?)new { queries = new[] { "q1" } })),
            CancellationToken.None);

        // Assert
        result.Should().Be("result");
        addedStep.Should().NotBeNull();
        addedStep!.Status.Should().Be(ArticleGenerationStepStatus.Running);
        addedStep.ArticleId.Should().Be(_articleId);
        addedStep.StepName.Should().Be("PlanQueries");
        addedStep.Sequence.Should().Be(1);
        addedStep.Model.Should().Be("test-model");
        addedStep.InputJson.Should().Contain("topic");

        updatedStep.Should().NotBeNull();
        updatedStep!.Status.Should().Be(ArticleGenerationStepStatus.Succeeded);
        updatedStep.FinishedAt.Should().NotBeNull();
        updatedStep.DurationMs.Should().NotBeNull();
        updatedStep.OutputJson.Should().Contain("queries");
    }

    [Fact]
    public async Task RecordAsync_MarksStepFailed_AndRethrows_OnException()
    {
        // Arrange
        var recorder = new PipelineStepRecorder(_repo.Object);
        ArticleGenerationStep? updatedStep = null;

        _repo.Setup(r => r.UpdateStepAsync(It.IsAny<ArticleGenerationStep>(), It.IsAny<CancellationToken>()))
             .Callback<ArticleGenerationStep, CancellationToken>((s, _) => updatedStep = s)
             .Returns(Task.CompletedTask);

        // Act
        var act = async () => await recorder.RecordAsync<string>(
            _articleId, "PlanQueries", 1, null, null,
            () => throw new InvalidOperationException("LLM failed"),
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("LLM failed");
        updatedStep.Should().NotBeNull();
        updatedStep!.Status.Should().Be(ArticleGenerationStepStatus.Failed);
        updatedStep.ErrorMessage.Should().Be("LLM failed");
        updatedStep.FinishedAt.Should().NotBeNull();
        updatedStep.DurationMs.Should().NotBeNull();
    }

    [Fact]
    public async Task RecordAsync_SerializesInputAndOutput()
    {
        // Arrange
        var recorder = new PipelineStepRecorder(_repo.Object);
        ArticleGenerationStep? addedStep = null;
        ArticleGenerationStep? updatedStep = null;

        _repo.Setup(r => r.AddStepAsync(It.IsAny<ArticleGenerationStep>(), It.IsAny<CancellationToken>()))
             .Callback<ArticleGenerationStep, CancellationToken>((s, _) => addedStep = s)
             .Returns(Task.CompletedTask);
        _repo.Setup(r => r.UpdateStepAsync(It.IsAny<ArticleGenerationStep>(), It.IsAny<CancellationToken>()))
             .Callback<ArticleGenerationStep, CancellationToken>((s, _) => updatedStep = s)
             .Returns(Task.CompletedTask);

        // Act
        await recorder.RecordAsync<string>(
            _articleId, "TestStep", 1, null,
            new { key = "inputValue" },
            () => Task.FromResult(("ok", (object?)new { key = "outputValue" })),
            CancellationToken.None);

        // Assert
        addedStep!.InputJson.Should().Contain("inputValue");
        updatedStep!.OutputJson.Should().Contain("outputValue");
    }
}
```

- [ ] **Step 4.2: Run test — expect failure (class not found)**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~PipelineStepRecorderTests" --nologo -q
```

Expected: Build error — `PipelineStepRecorder` does not exist yet.

- [ ] **Step 4.3: Create PipelineStepRecorder**

```csharp
// backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/PipelineStepRecorder.cs
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Anela.Heblo.Domain.Features.Article;

namespace Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline;

public sealed class PipelineStepRecorder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private readonly IArticleRepository _repository;

    public PipelineStepRecorder(IArticleRepository repository)
    {
        _repository = repository;
    }

    public async Task<T> RecordAsync<T>(
        Guid articleId,
        string stepName,
        int sequence,
        string? model,
        object? input,
        Func<Task<(T result, object? output)>> action,
        CancellationToken ct)
    {
        var step = new ArticleGenerationStep
        {
            Id = Guid.NewGuid(),
            ArticleId = articleId,
            StepName = stepName,
            Sequence = sequence,
            Status = ArticleGenerationStepStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
            Model = model,
            InputJson = SerializeOrNull(input),
        };

        await _repository.AddStepAsync(step, ct);

        var sw = Stopwatch.StartNew();
        try
        {
            var (result, output) = await action();
            sw.Stop();

            step.Status = ArticleGenerationStepStatus.Succeeded;
            step.FinishedAt = DateTimeOffset.UtcNow;
            step.DurationMs = sw.ElapsedMilliseconds;
            step.OutputJson = SerializeOrNull(output);

            await _repository.UpdateStepAsync(step, ct);
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            step.Status = ArticleGenerationStepStatus.Failed;
            step.FinishedAt = DateTimeOffset.UtcNow;
            step.DurationMs = sw.ElapsedMilliseconds;
            step.ErrorMessage = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;

            await _repository.UpdateStepAsync(step, CancellationToken.None);
            throw;
        }
    }

    private static string? SerializeOrNull(object? value) =>
        value is null ? null : JsonSerializer.Serialize(value, JsonOptions);
}
```

- [ ] **Step 4.4: Run tests — expect PASS**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~PipelineStepRecorderTests" --nologo -q
```

Expected: 3 tests passed.

- [ ] **Step 4.5: Register PipelineStepRecorder in ArticleModule**

In `backend/src/Anela.Heblo.Application/Features/Article/ArticleModule.cs`, add after `services.AddScoped<WriteArticleStep>();`:

```csharp
        services.AddScoped<PipelineStepRecorder>();
```

- [ ] **Step 4.6: Build to verify**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet build Anela.Heblo.sln --nologo -q
```

Expected: Build succeeded.

- [ ] **Step 4.7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/PipelineStepRecorder.cs \
        backend/src/Anela.Heblo.Application/Features/Article/ArticleModule.cs \
        backend/test/Anela.Heblo.Tests/Features/Article/PipelineStepRecorderTests.cs
git commit -m "feat(articles): add PipelineStepRecorder helper with tests"
```

---

## Task 5: Wrap PlanQueriesStep

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/PlanQueriesStep.cs`

- [ ] **Step 5.1: Update PlanQueriesStep to use recorder**

Replace the entire content of `PlanQueriesStep.cs`:

```csharp
using System.Text.Json.Serialization;
using Anela.Heblo.Application.Shared.Http;
using Anela.Heblo.Application.Shared.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline;

public class PlanQueriesStep : IArticlePipelineStep
{
    private const int MaxQueries = 8;

    private readonly IChatClient _chat;
    private readonly ArticleOptions _options;
    private readonly PipelineStepRecorder _recorder;
    private readonly ILogger<PlanQueriesStep> _logger;

    public PlanQueriesStep(
        IChatClient chat,
        IOptions<ArticleOptions> options,
        PipelineStepRecorder recorder,
        ILogger<PlanQueriesStep> logger)
    {
        _chat = chat;
        _options = options.Value;
        _recorder = recorder;
        _logger = logger;
    }

    public Task ExecuteAsync(ArticlePipelineContext context, CancellationToken ct)
    {
        var article = context.Article;
        var input = new { topic = article.Topic };

        return _recorder.RecordAsync<object?>(
            article.Id, "PlanQueries", 1, _options.QueryPlannerModel, input,
            async () =>
            {
                var queries = await RunAsync(article.Topic, ct);
                context.SearchQueries = queries;
                return (null, new { rawResponse = (string?)null, queries });
            },
            ct);
    }

    private async Task<List<string>> RunAsync(string topic, CancellationToken ct)
    {
        var chatOptions = new ChatOptions
        {
            ModelId = _options.QueryPlannerModel,
            MaxOutputTokens = 512
        };

        var response = await ChatRetry.RetryOnceAsync(
            () => _chat.GetResponseAsync(
                [
                    new ChatMessage(ChatRole.System, _options.QueryPlannerSystemPrompt),
                    new ChatMessage(ChatRole.User, topic)
                ],
                chatOptions,
                ct),
            _logger,
            ct);

        var raw = response.Text ?? string.Empty;
        var fallback = BuildFallback(topic);

        var parsed = JsonResponseParser.ParseOrFallback<QueryPlanOutput>(raw, new QueryPlanOutput([]), _logger);
        return parsed.Queries is { Count: > 0 }
            ? parsed.Queries.Take(MaxQueries).ToList()
            : fallback;
    }

    private static List<string> BuildFallback(string topic) =>
        [topic, $"{topic} statistiky", $"{topic} recenze"];

    private sealed record QueryPlanOutput(
        [property: JsonPropertyName("queries")] List<string> Queries);
}
```

> Note: We lose the raw LLM text in the output snapshot because `RunAsync` does not surface it. To capture the raw response, refactor `RunAsync` to return `(List<string> queries, string raw)`. See adjusted version:

Replace `RunAsync` return type and the recorder call:

```csharp
    public Task ExecuteAsync(ArticlePipelineContext context, CancellationToken ct)
    {
        var article = context.Article;
        var input = new { topic = article.Topic };

        return _recorder.RecordAsync<object?>(
            article.Id, "PlanQueries", 1, _options.QueryPlannerModel, input,
            async () =>
            {
                var (queries, raw) = await RunAsync(article.Topic, ct);
                context.SearchQueries = queries;
                return (null, new { rawResponse = raw, queries });
            },
            ct);
    }

    private async Task<(List<string> queries, string raw)> RunAsync(string topic, CancellationToken ct)
    {
        var chatOptions = new ChatOptions
        {
            ModelId = _options.QueryPlannerModel,
            MaxOutputTokens = 512
        };

        var response = await ChatRetry.RetryOnceAsync(
            () => _chat.GetResponseAsync(
                [
                    new ChatMessage(ChatRole.System, _options.QueryPlannerSystemPrompt),
                    new ChatMessage(ChatRole.User, topic)
                ],
                chatOptions,
                ct),
            _logger,
            ct);

        var raw = response.Text ?? string.Empty;
        var fallback = BuildFallback(topic);

        var parsed = JsonResponseParser.ParseOrFallback<QueryPlanOutput>(raw, new QueryPlanOutput([]), _logger);
        var queries = parsed.Queries is { Count: > 0 }
            ? parsed.Queries.Take(MaxQueries).ToList()
            : fallback;

        return (queries, raw);
    }
```

Use the second version (with raw capture). The final complete file is:

```csharp
using System.Text.Json.Serialization;
using Anela.Heblo.Application.Shared.Http;
using Anela.Heblo.Application.Shared.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline;

public class PlanQueriesStep : IArticlePipelineStep
{
    private const int MaxQueries = 8;

    private readonly IChatClient _chat;
    private readonly ArticleOptions _options;
    private readonly PipelineStepRecorder _recorder;
    private readonly ILogger<PlanQueriesStep> _logger;

    public PlanQueriesStep(
        IChatClient chat,
        IOptions<ArticleOptions> options,
        PipelineStepRecorder recorder,
        ILogger<PlanQueriesStep> logger)
    {
        _chat = chat;
        _options = options.Value;
        _recorder = recorder;
        _logger = logger;
    }

    public Task ExecuteAsync(ArticlePipelineContext context, CancellationToken ct)
    {
        var article = context.Article;

        return _recorder.RecordAsync<object?>(
            article.Id, "PlanQueries", 1, _options.QueryPlannerModel,
            new { topic = article.Topic },
            async () =>
            {
                var (queries, raw) = await RunAsync(article.Topic, ct);
                context.SearchQueries = queries;
                return (null, new { rawResponse = raw, queries });
            },
            ct);
    }

    private async Task<(List<string> queries, string raw)> RunAsync(string topic, CancellationToken ct)
    {
        var chatOptions = new ChatOptions
        {
            ModelId = _options.QueryPlannerModel,
            MaxOutputTokens = 512
        };

        var response = await ChatRetry.RetryOnceAsync(
            () => _chat.GetResponseAsync(
                [
                    new ChatMessage(ChatRole.System, _options.QueryPlannerSystemPrompt),
                    new ChatMessage(ChatRole.User, topic)
                ],
                chatOptions,
                ct),
            _logger,
            ct);

        var raw = response.Text ?? string.Empty;
        var fallback = BuildFallback(topic);

        var parsed = JsonResponseParser.ParseOrFallback<QueryPlanOutput>(raw, new QueryPlanOutput([]), _logger);
        var queries = parsed.Queries is { Count: > 0 }
            ? parsed.Queries.Take(MaxQueries).ToList()
            : fallback;

        return (queries, raw);
    }

    private static List<string> BuildFallback(string topic) =>
        [topic, $"{topic} statistiky", $"{topic} recenze"];

    private sealed record QueryPlanOutput(
        [property: JsonPropertyName("queries")] List<string> Queries);
}
```

- [ ] **Step 5.2: Build to verify**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet build Anela.Heblo.sln --nologo -q
```

Expected: Build succeeded.

- [ ] **Step 5.3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/PlanQueriesStep.cs
git commit -m "feat(articles): wrap PlanQueriesStep with PipelineStepRecorder"
```

---

## Task 6: Wrap GatherContextStep

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/GatherContextStep.cs`

- [ ] **Step 6.1: Add recorder to GatherContextStep**

Replace the entire content of `GatherContextStep.cs`:

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;
using Anela.Heblo.Application.Shared.WebSearch;
using Anela.Heblo.Domain.Features.Article;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using DomainArticle = Anela.Heblo.Domain.Features.Article.Article;

namespace Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline;

public class GatherContextStep : IArticlePipelineStep
{
    private readonly IMediator _mediator;
    private readonly IWebSearchClient _webSearch;
    private readonly IOneDriveService _oneDrive;
    private readonly ArticleOptions _options;
    private readonly PipelineStepRecorder _recorder;
    private readonly ILogger<GatherContextStep> _logger;

    public GatherContextStep(
        IMediator mediator,
        IWebSearchClient webSearch,
        IOneDriveService oneDrive,
        IOptions<ArticleOptions> options,
        PipelineStepRecorder recorder,
        ILogger<GatherContextStep> logger)
    {
        _mediator = mediator;
        _webSearch = webSearch;
        _oneDrive = oneDrive;
        _options = options.Value;
        _recorder = recorder;
        _logger = logger;
    }

    public Task ExecuteAsync(ArticlePipelineContext context, CancellationToken ct)
    {
        var article = context.Article;

        return _recorder.RecordAsync<object?>(
            article.Id, "GatherContext", 2, null,
            new { queries = context.SearchQueries },
            async () =>
            {
                await GatherAsync(context, ct);
                return (null, new
                {
                    snippetCount = context.ContextSnippets.Count,
                    snippets = context.ContextSnippets,
                    styleGuideLength = context.StyleGuideText?.Length,
                });
            },
            ct);
    }

    private async Task GatherAsync(ArticlePipelineContext context, CancellationToken ct)
    {
        var article = context.Article;

        var kbTask = article.UsedKnowledgeBase
            ? GatherKnowledgeBaseSnippetsAsync(context.SearchQueries, ct)
            : Task.FromResult<List<ContextSnippet>>([]);

        var webTask = article.UsedWebSearch
            ? GatherWebSnippetsAsync(context.SearchQueries, ct)
            : Task.FromResult<List<ContextSnippet>>([]);

        var styleGuideTask = HasStyleGuide(article)
            ? LoadStyleGuideAsync(article, ct)
            : Task.FromResult<string?>(null);

        await Task.WhenAll(kbTask, webTask, styleGuideTask);

        var kbSnippets = kbTask.Result;
        var webSnippets = webTask.Result;
        var styleGuideText = styleGuideTask.Result;

        var deduplicatedWeb = DeduplicateByUrl(webSnippets);

        context.ContextSnippets = [.. kbSnippets, .. deduplicatedWeb];
        context.StyleGuideText = styleGuideText;
    }

    private async Task<List<ContextSnippet>> GatherKnowledgeBaseSnippetsAsync(
        List<string> queries,
        CancellationToken ct)
    {
        var snippets = new List<ContextSnippet>();

        foreach (var query in queries)
        {
            try
            {
                var response = await _mediator.Send(
                    new SearchDocumentsRequest { Query = query, TopK = _options.KnowledgeBaseTopK },
                    ct);

                snippets.AddRange(response.Chunks.Select(chunk => new ContextSnippet
                {
                    Source = SourceType.KnowledgeBase,
                    Title = chunk.SourceFilename,
                    Excerpt = chunk.Content,
                    Url = null,
                    ChunkId = chunk.ChunkId,
                    Score = chunk.Score
                }));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "KB search failed for query '{Query}'", query);
            }
        }

        return snippets;
    }

    private async Task<List<ContextSnippet>> GatherWebSnippetsAsync(
        List<string> queries,
        CancellationToken ct)
    {
        var snippets = new List<ContextSnippet>();

        foreach (var query in queries)
        {
            try
            {
                var result = await _webSearch.SearchAsync(
                    query,
                    new WebSearchOptions { Locale = "cs", Geo = "cz", Top = _options.WebSearchTopK },
                    ct);

                snippets.AddRange(result.Hits.Select(hit => new ContextSnippet
                {
                    Source = SourceType.Web,
                    Title = hit.Title,
                    Excerpt = hit.Snippet,
                    Url = hit.Url
                }));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Web search failed for query '{Query}'", query);
            }
        }

        return snippets;
    }

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

    private static bool HasStyleGuide(DomainArticle article) =>
        article.StyleGuideDriveId != null && article.StyleGuideItemPath != null;

    private static List<ContextSnippet> DeduplicateByUrl(List<ContextSnippet> snippets)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ContextSnippet>();

        foreach (var snippet in snippets)
        {
            if (snippet.Url == null || seen.Add(snippet.Url))
                result.Add(snippet);
        }

        return result;
    }
}
```

- [ ] **Step 6.2: Build to verify**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet build Anela.Heblo.sln --nologo -q
```

Expected: Build succeeded.

- [ ] **Step 6.3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/GatherContextStep.cs
git commit -m "feat(articles): wrap GatherContextStep with PipelineStepRecorder"
```

---

## Task 7: Wrap AggregateFactsStep

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/AggregateFactsStep.cs`

- [ ] **Step 7.1: Update AggregateFactsStep**

Replace the entire content:

```csharp
using System.Text;
using System.Text.Json.Serialization;
using Anela.Heblo.Application.Shared.Http;
using Anela.Heblo.Application.Shared.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline;

public class AggregateFactsStep : IArticlePipelineStep
{
    private const int MaxSnippets = 50;

    private readonly IChatClient _chat;
    private readonly ArticleOptions _options;
    private readonly PipelineStepRecorder _recorder;
    private readonly ILogger<AggregateFactsStep> _logger;

    public AggregateFactsStep(
        IChatClient chat,
        IOptions<ArticleOptions> options,
        PipelineStepRecorder recorder,
        ILogger<AggregateFactsStep> logger)
    {
        _chat = chat;
        _options = options.Value;
        _recorder = recorder;
        _logger = logger;
    }

    public Task ExecuteAsync(ArticlePipelineContext context, CancellationToken ct)
    {
        var article = context.Article;
        var snippets = context.ContextSnippets.Take(MaxSnippets).ToList();

        return _recorder.RecordAsync<object?>(
            article.Id, "AggregateFacts", 3, _options.AggregateFactsModel,
            new { snippetCount = snippets.Count, topic = article.Topic, angle = article.Angle },
            async () =>
            {
                var (facts, raw, summary, gaps) = await RunAsync(article, snippets, ct);
                context.Facts = facts;
                return (null, new { rawResponse = raw, facts, summary, gaps });
            },
            ct);
    }

    private async Task<(List<AggregatedFact> facts, string raw, string? summary, string? gaps)> RunAsync(
        Domain.Features.Article.Article article,
        List<ContextSnippet> snippets,
        CancellationToken ct)
    {
        var userMessage = BuildUserMessage(article.Topic, article.Angle, article.Scope, snippets);

        var chatOptions = new ChatOptions
        {
            ModelId = _options.AggregateFactsModel,
            MaxOutputTokens = _options.AggregateMaxTokens
        };

        var response = await ChatRetry.RetryOnceAsync(
            () => _chat.GetResponseAsync(
                [
                    new ChatMessage(ChatRole.System, _options.AggregateFactsSystemPrompt),
                    new ChatMessage(ChatRole.User, userMessage)
                ],
                chatOptions,
                ct),
            _logger,
            ct);

        var raw = response.Text ?? string.Empty;
        var fallback = BuildFallback(snippets);

        var parsed = JsonResponseParser.ParseOrFallback<AggregateOutput>(raw, fallback, _logger);

        var facts = (parsed.Facts ?? [])
            .Select(dto => new AggregatedFact
            {
                Claim = dto.Claim,
                Confidence = dto.Confidence,
                SourceUrl = dto.SourceUrl,
                SourceTitle = dto.SourceTitle
            })
            .ToList();

        return (facts, raw, parsed.Summary, parsed.Gaps);
    }

    private static string BuildUserMessage(
        string topic,
        string? angle,
        string scope,
        List<ContextSnippet> snippets)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Téma: {topic}");
        sb.AppendLine($"Úhel: {angle ?? "(nevyspecifikováno)"}");
        sb.AppendLine($"Rozsah: {scope}");
        sb.AppendLine();
        sb.AppendLine("Zdroje:");

        for (var i = 0; i < snippets.Count; i++)
        {
            var s = snippets[i];
            sb.AppendLine($"{i + 1}. [{s.Title}] {s.Excerpt}");
        }

        return sb.ToString();
    }

    private static AggregateOutput BuildFallback(List<ContextSnippet> snippets)
    {
        var summary = string.Join(" ", snippets.Select(s => s.Excerpt));
        if (summary.Length > 2000)
            summary = summary[..2000];

        return new AggregateOutput(null, summary, null);
    }

    private sealed record AggregateOutput(
        [property: JsonPropertyName("facts")] List<FactDto>? Facts,
        [property: JsonPropertyName("summary")] string? Summary,
        [property: JsonPropertyName("gaps")] string? Gaps);

    private sealed record FactDto(
        [property: JsonPropertyName("claim")] string Claim,
        [property: JsonPropertyName("confidence")] double Confidence,
        [property: JsonPropertyName("source_url")] string? SourceUrl,
        [property: JsonPropertyName("source_title")] string? SourceTitle);
}
```

- [ ] **Step 7.2: Build and commit**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet build Anela.Heblo.sln --nologo -q
git add backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/AggregateFactsStep.cs
git commit -m "feat(articles): wrap AggregateFactsStep with PipelineStepRecorder; preserve summary and gaps"
```

---

## Task 8: Wrap ValidateFactsStep

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/ValidateFactsStep.cs`

- [ ] **Step 8.1: Update ValidateFactsStep**

Replace the entire content:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using Anela.Heblo.Application.Shared.Http;
using Anela.Heblo.Application.Shared.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline;

public class ValidateFactsStep : IArticlePipelineStep
{
    private readonly IChatClient _chat;
    private readonly ArticleOptions _options;
    private readonly PipelineStepRecorder _recorder;
    private readonly ILogger<ValidateFactsStep> _logger;

    public ValidateFactsStep(
        IChatClient chat,
        IOptions<ArticleOptions> options,
        PipelineStepRecorder recorder,
        ILogger<ValidateFactsStep> logger)
    {
        _chat = chat;
        _options = options.Value;
        _recorder = recorder;
        _logger = logger;
    }

    public Task ExecuteAsync(ArticlePipelineContext context, CancellationToken ct)
    {
        if (context.Facts.Count == 0)
            return Task.CompletedTask;

        var article = context.Article;

        return _recorder.RecordAsync<object?>(
            article.Id, "ValidateFacts", 4, _options.ValidateFactsModel,
            new { factCount = context.Facts.Count, claims = context.Facts.Select(f => f.Claim).ToList() },
            async () =>
            {
                var (updatedFacts, raw, validatedFacts) = await RunAsync(context.Facts, ct);
                context.Facts = updatedFacts;
                return (null, new { rawResponse = raw, validatedFacts });
            },
            ct);
    }

    private async Task<(List<AggregatedFact> updatedFacts, string raw, List<ValidatedFactDto>? validatedFacts)> RunAsync(
        List<AggregatedFact> facts,
        CancellationToken ct)
    {
        try
        {
            var claims = facts.Select(f => f.Claim).ToList();
            var userMessage = JsonSerializer.Serialize(claims);

            var chatOptions = new ChatOptions { ModelId = _options.ValidateFactsModel };

            var response = await ChatRetry.RetryOnceAsync(
                () => _chat.GetResponseAsync(
                    [
                        new ChatMessage(ChatRole.System, _options.ValidateFactsSystemPrompt),
                        new ChatMessage(ChatRole.User, userMessage)
                    ],
                    chatOptions,
                    ct),
                _logger,
                ct);

            var raw = response.Text ?? string.Empty;
            var parsed = JsonResponseParser.ParseOrFallback<ValidatedFactsOutput>(
                raw,
                new ValidatedFactsOutput(null),
                _logger);

            if (parsed.ValidatedFacts == null)
                return (facts, raw, null);

            return (ApplyValidationNotes(facts, parsed.ValidatedFacts), raw, parsed.ValidatedFacts);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Fact validation failed; original facts preserved");
            return (facts, "", null);
        }
    }

    private static List<AggregatedFact> ApplyValidationNotes(
        List<AggregatedFact> facts,
        List<ValidatedFactDto> validatedFacts)
    {
        return facts
            .Select((f, i) => i < validatedFacts.Count
                ? f with { ValidationNote = validatedFacts[i].Note }
                : f)
            .ToList();
    }

    private sealed record ValidatedFactsOutput(
        [property: JsonPropertyName("validated_facts")] List<ValidatedFactDto>? ValidatedFacts);

    private sealed record ValidatedFactDto(
        [property: JsonPropertyName("fact")] string Fact,
        [property: JsonPropertyName("note")] string? Note,
        [property: JsonPropertyName("reliable")] bool Reliable);
}
```

- [ ] **Step 8.2: Build and commit**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet build Anela.Heblo.sln --nologo -q
git add backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/ValidateFactsStep.cs
git commit -m "feat(articles): wrap ValidateFactsStep with PipelineStepRecorder; persist reliable flag"
```

---

## Task 9: Wrap WriteArticleStep

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/WriteArticleStep.cs`

- [ ] **Step 9.1: Update WriteArticleStep**

Replace the entire content:

```csharp
using System.Text;
using System.Text.Json.Serialization;
using Anela.Heblo.Application.Shared.Http;
using Anela.Heblo.Application.Shared.Json;
using Anela.Heblo.Domain.Features.Article;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline;

public class WriteArticleStep : IArticlePipelineStep
{
    private readonly IChatClient _chat;
    private readonly ArticleOptions _options;
    private readonly PipelineStepRecorder _recorder;
    private readonly ILogger<WriteArticleStep> _logger;

    public WriteArticleStep(
        IChatClient chat,
        IOptions<ArticleOptions> options,
        PipelineStepRecorder recorder,
        ILogger<WriteArticleStep> logger)
    {
        _chat = chat;
        _options = options.Value;
        _recorder = recorder;
        _logger = logger;
    }

    public Task ExecuteAsync(ArticlePipelineContext context, CancellationToken ct)
    {
        var article = context.Article;

        return _recorder.RecordAsync<object?>(
            article.Id, "WriteArticle", 5, _options.DefaultModel,
            new { factCount = context.Facts.Count, snippetCount = context.ContextSnippets.Count },
            async () =>
            {
                var (title, html, sourceRefs, raw, sourcesUsed) = await RunAsync(context, ct);
                context.GeneratedTitle = title;
                context.GeneratedHtml = html;
                context.SourceRefs = sourceRefs;
                return (null, new { rawResponse = raw, articleTitle = title, sourcesUsed });
            },
            ct);
    }

    private async Task<(string? title, string? html, List<ArticleSourceRef> sourceRefs, string raw, List<SourceUsedDto>? sourcesUsed)> RunAsync(
        ArticlePipelineContext context,
        CancellationToken ct)
    {
        var article = context.Article;
        var systemPrompt = BuildSystemPrompt(context.StyleGuideText);
        var userMessage = BuildUserMessage(context);

        var chatOptions = new ChatOptions
        {
            ModelId = _options.DefaultModel,
            MaxOutputTokens = _options.WriteMaxTokens
        };

        var response = await ChatRetry.RetryOnceAsync(
            () => _chat.GetResponseAsync(
                [
                    new ChatMessage(ChatRole.System, systemPrompt),
                    new ChatMessage(ChatRole.User, userMessage)
                ],
                chatOptions,
                ct),
            _logger,
            ct);

        var raw = response.Text ?? string.Empty;

        var fallback = new WriteArticleOutput(article.Topic, $"<p>{raw}</p>", null);
        var parsed = JsonResponseParser.ParseOrFallback<WriteArticleOutput>(raw, fallback, _logger);

        var title = parsed.ArticleTitle ?? article.Topic;
        var html = parsed.ArticleHtml ?? $"<p>{raw}</p>";
        var sourceRefs = MapSources(parsed.SourcesUsed, context.ContextSnippets, context.Facts);

        return (title, html, sourceRefs, raw, parsed.SourcesUsed);
    }

    private const string SystemInstruction =
        """
        Jsi zkušený redaktor kosmetického obsahu. Píšeš výhradně v češtině.
        Odpověz POUZE validním JSON bez markdown nebo code fences.
        V poli article_html použij výhradně HTML tagy – nikdy nepište doslovný text "\n" jako obsah.
        {"article_title":"...","article_html":"<article>...</article>","sources_used":[{"title":"...","url":"..."}]}
        """;

    private static string BuildSystemPrompt(string? styleGuideText)
    {
        if (styleGuideText == null)
            return SystemInstruction;

        return $"STYLE GUIDE — follow this exactly:\n{styleGuideText}\n\n{SystemInstruction}";
    }

    private string BuildUserMessage(ArticlePipelineContext context)
    {
        var article = context.Article;
        var factsText = BuildFactsList(context.Facts);

        return _options.WriteArticleSystemPromptTemplate
            .Replace("{topic}", article.Topic)
            .Replace("{audience}", article.Audience ?? "obecné publikum")
            .Replace("{length}", article.Length)
            .Replace("{angle}", article.Angle ?? "(nevyspecifikováno)")
            .Replace("{facts}", factsText)
            .Replace("{style_guide}", context.StyleGuideText ?? "");
    }

    private static string BuildFactsList(List<AggregatedFact> facts)
    {
        if (facts.Count == 0)
            return "(žádná fakta)";

        var sb = new StringBuilder();
        for (var i = 0; i < facts.Count; i++)
        {
            var fact = facts[i];
            sb.Append($"{i + 1}. {fact.Claim}");

            if (fact.SourceTitle != null || fact.SourceUrl != null)
            {
                var source = fact.SourceTitle ?? fact.SourceUrl;
                sb.Append($" [zdroj: {source}]");
            }

            if (fact.ValidationNote != null)
                sb.Append($" (pozn.: {fact.ValidationNote})");

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static List<ArticleSourceRef> MapSources(
        List<SourceUsedDto>? sourcesUsed,
        List<ContextSnippet> snippets,
        List<AggregatedFact> facts)
    {
        if (sourcesUsed == null)
            return [];

        return sourcesUsed.Select(source =>
        {
            var snippetMatch = snippets.FirstOrDefault(s =>
                string.Equals(s.Title, source.Title, StringComparison.OrdinalIgnoreCase));
            var factMatch = facts.FirstOrDefault(f =>
                string.Equals(f.SourceTitle, source.Title, StringComparison.OrdinalIgnoreCase));

            return new ArticleSourceRef(
                Title: source.Title,
                Url: source.Url,
                Type: source.Url != null ? SourceType.Web : SourceType.KnowledgeBase,
                ChunkId: snippetMatch?.ChunkId,
                Confidence: snippetMatch?.Score,
                Excerpt: TruncateExcerpt(factMatch?.Claim),
                ValidationNote: factMatch?.ValidationNote);
        }).ToList();
    }

    private static string? TruncateExcerpt(string? claim)
    {
        if (string.IsNullOrEmpty(claim))
            return null;
        return claim.Length <= 200 ? claim : claim[..200];
    }

    private sealed record WriteArticleOutput(
        [property: JsonPropertyName("article_title")] string? ArticleTitle,
        [property: JsonPropertyName("article_html")] string? ArticleHtml,
        [property: JsonPropertyName("sources_used")] List<SourceUsedDto>? SourcesUsed);

    private sealed record SourceUsedDto(
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("url")] string? Url);
}
```

- [ ] **Step 9.2: Build and commit**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet build Anela.Heblo.sln --nologo -q
git add backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/WriteArticleStep.cs
git commit -m "feat(articles): wrap WriteArticleStep with PipelineStepRecorder"
```

---

## Task 10: GetArticleTrace use case + controller endpoint

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Article/UseCases/GetArticleTrace/GetArticleTraceRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Article/UseCases/GetArticleTrace/GetArticleTraceResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Article/UseCases/GetArticleTrace/GetArticleTraceHandler.cs`
- Modify: `backend/src/Anela.Heblo.API/Controllers/ArticlesController.cs`

- [ ] **Step 10.1: Write the failing tests**

Create `backend/test/Anela.Heblo.Tests/Features/Article/GetArticleTraceHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Article.UseCases.GetArticleTrace;
using Anela.Heblo.Domain.Features.Article;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Article;

public class GetArticleTraceHandlerTests
{
    private readonly Mock<IArticleRepository> _repo = new();

    [Fact]
    public async Task Handle_ReturnsSteps_WhenArticleExists()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var article = new Domain.Features.Article.Article
        {
            Id = articleId,
            Topic = "Test Topic",
            Steps =
            [
                new ArticleGenerationStep { Id = Guid.NewGuid(), ArticleId = articleId, StepName = "PlanQueries", Sequence = 1, Status = ArticleGenerationStepStatus.Succeeded, StartedAt = DateTimeOffset.UtcNow },
                new ArticleGenerationStep { Id = Guid.NewGuid(), ArticleId = articleId, StepName = "GatherContext", Sequence = 2, Status = ArticleGenerationStepStatus.Succeeded, StartedAt = DateTimeOffset.UtcNow },
            ]
        };

        _repo.Setup(r => r.GetWithStepsAsync(articleId, default)).ReturnsAsync(article);

        var handler = new GetArticleTraceHandler(_repo.Object);

        // Act
        var result = await handler.Handle(new GetArticleTraceRequest { Id = articleId }, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.ArticleId.Should().Be(articleId);
        result.Steps.Should().HaveCount(2);
        result.Steps[0].StepName.Should().Be("PlanQueries");
        result.Steps[1].StepName.Should().Be("GatherContext");
    }

    [Fact]
    public async Task Handle_Returns404Envelope_WhenArticleMissing()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        _repo.Setup(r => r.GetWithStepsAsync(articleId, default)).ReturnsAsync((Domain.Features.Article.Article?)null);

        var handler = new GetArticleTraceHandler(_repo.Object);

        // Act
        var result = await handler.Handle(new GetArticleTraceRequest { Id = articleId }, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(Application.Shared.ErrorCodes.ArticleNotFound);
    }

    [Fact]
    public async Task Handle_OrdersStepsBySequence()
    {
        // Arrange
        var articleId = Guid.NewGuid();
        var article = new Domain.Features.Article.Article
        {
            Id = articleId,
            Topic = "Test",
            Steps =
            [
                new ArticleGenerationStep { Id = Guid.NewGuid(), ArticleId = articleId, StepName = "WriteArticle", Sequence = 5, Status = ArticleGenerationStepStatus.Succeeded, StartedAt = DateTimeOffset.UtcNow },
                new ArticleGenerationStep { Id = Guid.NewGuid(), ArticleId = articleId, StepName = "PlanQueries", Sequence = 1, Status = ArticleGenerationStepStatus.Succeeded, StartedAt = DateTimeOffset.UtcNow },
            ]
        };

        _repo.Setup(r => r.GetWithStepsAsync(articleId, default)).ReturnsAsync(article);

        var handler = new GetArticleTraceHandler(_repo.Object);

        // Act
        var result = await handler.Handle(new GetArticleTraceRequest { Id = articleId }, CancellationToken.None);

        // Assert
        result.Steps.Should().BeInAscendingOrder(s => s.Sequence);
    }
}
```

- [ ] **Step 10.2: Run test — expect failure (class not found)**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetArticleTraceHandlerTests" --nologo -q
```

Expected: Build errors — classes not found.

- [ ] **Step 10.3: Create the request**

```csharp
// backend/src/Anela.Heblo.Application/Features/Article/UseCases/GetArticleTrace/GetArticleTraceRequest.cs
using MediatR;

namespace Anela.Heblo.Application.Features.Article.UseCases.GetArticleTrace;

public class GetArticleTraceRequest : IRequest<GetArticleTraceResponse>
{
    public Guid Id { get; set; }
}
```

- [ ] **Step 10.4: Create the response DTOs (classes, not records — CLAUDE.md)**

```csharp
// backend/src/Anela.Heblo.Application/Features/Article/UseCases/GetArticleTrace/GetArticleTraceResponse.cs
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Article.UseCases.GetArticleTrace;

public sealed class GetArticleTraceResponse : BaseResponse
{
    public Guid ArticleId { get; set; }
    public List<ArticleGenerationStepDto> Steps { get; set; } = [];

    public GetArticleTraceResponse() { }

    public GetArticleTraceResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}

public sealed class ArticleGenerationStepDto
{
    public Guid Id { get; set; }
    public string StepName { get; set; } = "";
    public int Sequence { get; set; }
    public string Status { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public long? DurationMs { get; set; }
    public string? Model { get; set; }
    public string? InputJson { get; set; }
    public string? OutputJson { get; set; }
    public string? ErrorMessage { get; set; }
}
```

- [ ] **Step 10.5: Create the handler**

```csharp
// backend/src/Anela.Heblo.Application/Features/Article/UseCases/GetArticleTrace/GetArticleTraceHandler.cs
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Article;
using MediatR;

namespace Anela.Heblo.Application.Features.Article.UseCases.GetArticleTrace;

public sealed class GetArticleTraceHandler : IRequestHandler<GetArticleTraceRequest, GetArticleTraceResponse>
{
    private readonly IArticleRepository _repository;

    public GetArticleTraceHandler(IArticleRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetArticleTraceResponse> Handle(
        GetArticleTraceRequest request,
        CancellationToken cancellationToken)
    {
        var article = await _repository.GetWithStepsAsync(request.Id, cancellationToken);
        if (article == null)
        {
            return new GetArticleTraceResponse(ErrorCodes.ArticleNotFound,
                new Dictionary<string, string> { { "id", request.Id.ToString() } });
        }

        return new GetArticleTraceResponse
        {
            ArticleId = article.Id,
            Steps = article.Steps
                .OrderBy(s => s.Sequence)
                .Select(s => new ArticleGenerationStepDto
                {
                    Id = s.Id,
                    StepName = s.StepName,
                    Sequence = s.Sequence,
                    Status = s.Status.ToString(),
                    StartedAt = s.StartedAt,
                    FinishedAt = s.FinishedAt,
                    DurationMs = s.DurationMs,
                    Model = s.Model,
                    InputJson = s.InputJson,
                    OutputJson = s.OutputJson,
                    ErrorMessage = s.ErrorMessage,
                })
                .ToList(),
        };
    }
}
```

- [ ] **Step 10.6: Run tests — expect PASS**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetArticleTraceHandlerTests" --nologo -q
```

Expected: 3 tests passed.

- [ ] **Step 10.7: Add controller endpoint**

In `ArticlesController.cs`, add after the existing `GetById` action, using the existing pattern:

```csharp
    [HttpGet("{id:guid}/trace")]
    public async Task<ActionResult<GetArticleTraceResponse>> GetTrace(
        Guid id,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetArticleTraceRequest { Id = id }, ct);
        return HandleResponse(result);
    }
```

Also add the using at the top:
```csharp
using Anela.Heblo.Application.Features.Article.UseCases.GetArticleTrace;
```

- [ ] **Step 10.8: Build to verify**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet build Anela.Heblo.sln --nologo -q
```

Expected: Build succeeded.

- [ ] **Step 10.9: Run all Article tests**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Features.Article" --nologo -q
```

Expected: All tests pass.

- [ ] **Step 10.10: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Article/UseCases/GetArticleTrace/ \
        backend/src/Anela.Heblo.API/Controllers/ArticlesController.cs \
        backend/test/Anela.Heblo.Tests/Features/Article/GetArticleTraceHandlerTests.cs
git commit -m "feat(articles): add GetArticleTrace use case and GET /api/articles/{id}/trace endpoint"
```

---

## Task 11: Frontend — useArticleTrace hook

**Files:**
- Create: `frontend/src/api/hooks/useArticleTrace.ts`

- [ ] **Step 11.1: Generate updated OpenAPI TypeScript client**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/frontend
npm run build 2>&1 | tail -20
```

Expected: Build succeeds and regenerates `src/api/generated/api-client` with the new trace endpoint.

> If the client generator runs as part of `npm run build`, the new `articles_GetTrace` method will appear in `api-client`. If it does not (generator not wired to build), check `package.json` scripts and run the generator manually per `docs/development/api-client-generation.md`.

- [ ] **Step 11.2: Create the hook**

```typescript
// frontend/src/api/hooks/useArticleTrace.ts
import { useQuery } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../client';
import { articleKeys } from './useArticles';

export interface ArticleGenerationStepResult {
  id: string;
  stepName: string;
  sequence: number;
  status: string;
  startedAt: string;
  finishedAt: string | null;
  durationMs: number | null;
  model: string | null;
  inputJson: string | null;
  outputJson: string | null;
  errorMessage: string | null;
}

export interface ArticleTraceResult {
  articleId: string;
  steps: ArticleGenerationStepResult[];
}

export const articleTraceKeys = {
  trace: (id: string) => [...articleKeys.all, 'trace', id] as const,
};

export function useArticleTrace(articleId: string, enabled: boolean) {
  return useQuery({
    queryKey: articleTraceKeys.trace(articleId),
    queryFn: async (): Promise<ArticleTraceResult> => {
      const apiClient = getAuthenticatedApiClient();
      const fullUrl = `${(apiClient as any).baseUrl}/api/articles/${articleId}/trace`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'GET',
        headers: { Accept: 'application/json' },
      });

      if (!response.ok) {
        throw new Error(`Failed to fetch article trace: ${response.status}`);
      }

      const data = await response.json();
      return {
        articleId: data.articleId ?? '',
        steps: (data.steps ?? []).map((s: any) => ({
          id: s.id ?? '',
          stepName: s.stepName ?? '',
          sequence: s.sequence ?? 0,
          status: s.status ?? '',
          startedAt: s.startedAt ?? '',
          finishedAt: s.finishedAt ?? null,
          durationMs: s.durationMs ?? null,
          model: s.model ?? null,
          inputJson: s.inputJson ?? null,
          outputJson: s.outputJson ?? null,
          errorMessage: s.errorMessage ?? null,
        })),
      };
    },
    enabled: enabled && !!articleId,
    staleTime: 60_000,
    gcTime: 5 * 60 * 1000,
  });
}
```

- [ ] **Step 11.3: TypeScript check**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/frontend
npx tsc --noEmit 2>&1 | head -30
```

Expected: No errors.

- [ ] **Step 11.4: Commit**

```bash
git add frontend/src/api/hooks/useArticleTrace.ts
git commit -m "feat(articles): add useArticleTrace React Query hook"
```

---

## Task 12: Frontend — ArticleDebugPanel component

**Files:**
- Create: `frontend/src/features/articles/ArticleDebugPanel.tsx`
- Modify: `frontend/src/features/articles/ArticleDetail.tsx`

- [ ] **Step 12.1: Create ArticleDebugPanel**

```tsx
// frontend/src/features/articles/ArticleDebugPanel.tsx
import { useState } from 'react';
import { ChevronDown, ChevronRight } from 'lucide-react';
import { useArticleTrace, ArticleGenerationStepResult } from '../../api/hooks/useArticleTrace';

interface ArticleDebugPanelProps {
  articleId: string;
}

const STATUS_COLORS: Record<string, string> = {
  Running: 'bg-blue-100 text-blue-700',
  Succeeded: 'bg-green-100 text-green-700',
  Failed: 'bg-red-100 text-red-700',
};

const STATUS_LABELS: Record<string, string> = {
  Running: 'Běží',
  Succeeded: 'Dokončeno',
  Failed: 'Chyba',
};

function prettyJson(raw: string | null): string {
  if (!raw) return '';
  try {
    return JSON.stringify(JSON.parse(raw), null, 2);
  } catch {
    return raw;
  }
}

function StepCard({ step }: { step: ArticleGenerationStepResult }) {
  const [inputOpen, setInputOpen] = useState(false);
  const [outputOpen, setOutputOpen] = useState(false);

  const statusColor = STATUS_COLORS[step.status] ?? 'bg-gray-100 text-gray-700';
  const statusLabel = STATUS_LABELS[step.status] ?? step.status;

  return (
    <div className="border border-gray-200 rounded p-3 text-sm">
      <div className="flex items-center gap-2 flex-wrap">
        <span className="font-mono font-medium text-gray-700">{step.sequence}. {step.stepName}</span>
        <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${statusColor}`}>
          {statusLabel}
        </span>
        {step.model && (
          <span className="text-xs text-gray-400">{step.model}</span>
        )}
        {step.durationMs != null && (
          <span className="text-xs text-gray-400">{step.durationMs} ms</span>
        )}
      </div>

      {step.errorMessage && (
        <p className="mt-2 text-xs text-red-600">{step.errorMessage}</p>
      )}

      {step.inputJson && (
        <div className="mt-2">
          <button
            type="button"
            onClick={() => setInputOpen((v) => !v)}
            className="flex items-center gap-1 text-xs text-gray-500 hover:text-gray-700"
          >
            {inputOpen ? <ChevronDown className="w-3 h-3" /> : <ChevronRight className="w-3 h-3" />}
            Vstup
          </button>
          {inputOpen && (
            <pre className="mt-1 text-xs bg-gray-50 rounded p-2 overflow-x-auto whitespace-pre-wrap break-all">
              {prettyJson(step.inputJson)}
            </pre>
          )}
        </div>
      )}

      {step.outputJson && (
        <div className="mt-2">
          <button
            type="button"
            onClick={() => setOutputOpen((v) => !v)}
            className="flex items-center gap-1 text-xs text-gray-500 hover:text-gray-700"
          >
            {outputOpen ? <ChevronDown className="w-3 h-3" /> : <ChevronRight className="w-3 h-3" />}
            Výstup
          </button>
          {outputOpen && (
            <pre className="mt-1 text-xs bg-gray-50 rounded p-2 overflow-x-auto whitespace-pre-wrap break-all">
              {prettyJson(step.outputJson)}
            </pre>
          )}
        </div>
      )}
    </div>
  );
}

export default function ArticleDebugPanel({ articleId }: ArticleDebugPanelProps) {
  const [open, setOpen] = useState(false);
  const { data, isLoading, error } = useArticleTrace(articleId, open);

  return (
    <div className="mt-6 border-t border-gray-100 pt-4">
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        className="flex items-center gap-1 text-sm text-gray-500 hover:text-gray-700"
      >
        {open ? <ChevronDown className="w-4 h-4" /> : <ChevronRight className="w-4 h-4" />}
        Debug — průběh generování
      </button>

      {open && (
        <div className="mt-3">
          {isLoading && (
            <p className="text-xs text-gray-400">Načítám…</p>
          )}
          {error && (
            <p className="text-xs text-red-600">Nepodařilo se načíst debug informace.</p>
          )}
          {data && data.steps.length === 0 && (
            <p className="text-xs text-gray-400">Žádná data.</p>
          )}
          {data && data.steps.length > 0 && (
            <div className="flex flex-col gap-2">
              {data.steps.map((step) => (
                <StepCard key={step.id} step={step} />
              ))}
            </div>
          )}
        </div>
      )}
    </div>
  );
}
```

- [ ] **Step 12.2: Add ArticleDebugPanel to ArticleDetail**

In `ArticleDetail.tsx`, add the import at the top:

```typescript
import ArticleDebugPanel from './ArticleDebugPanel';
```

In the `ArticleView` component, after the `<ArticleFeedbackSection article={article} />` line, add:

```tsx
      <ArticleDebugPanel articleId={article.id} />
```

The updated `ArticleView` function body becomes:

```tsx
function ArticleView({ article }: { article: ArticleDetailType }) {
  return (
    <div>
      <div className="mb-4">
        {article.title && (
          <h2 className="text-xl font-semibold text-gray-900 mb-1">{article.title}</h2>
        )}
        <p className="text-sm text-gray-500">{article.topic}</p>
        <div className="flex flex-wrap gap-2 mt-2 text-xs text-gray-500">
          <span>{article.scope}</span>
          <span>·</span>
          <span>{article.length}</span>
          {article.useKnowledgeBase && <span>· Znalostní báze</span>}
          {article.useWebSearch && <span>· Webové vyhledávání</span>}
        </div>
      </div>

      {article.htmlContent && <HtmlContent html={article.htmlContent} />}
      <ArticleSourceList sources={article.sources} />
      <ArticleFeedbackSection article={article} />
      <ArticleDebugPanel articleId={article.id} />
    </div>
  );
}
```

- [ ] **Step 12.3: TypeScript check + lint**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/frontend
npx tsc --noEmit 2>&1 | head -30
npm run lint 2>&1 | tail -20
```

Expected: No errors.

- [ ] **Step 12.4: Build**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/frontend
npm run build 2>&1 | tail -20
```

Expected: Build succeeded.

- [ ] **Step 12.5: Commit**

```bash
git add frontend/src/features/articles/ArticleDebugPanel.tsx \
        frontend/src/features/articles/ArticleDetail.tsx
git commit -m "feat(articles): add collapsible ArticleDebugPanel showing per-step generation trace"
```

---

## Task 13: Final verification

- [ ] **Step 13.1: Full backend build + format check**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet build Anela.Heblo.sln --nologo -q
dotnet format Anela.Heblo.sln --verify-no-changes 2>&1
```

Expected: Build succeeded, format diff is empty.

- [ ] **Step 13.2: Run all tests**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --nologo -q
```

Expected: All tests pass, 0 failed.

- [ ] **Step 13.3: Full frontend build + lint**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/frontend
npm run build 2>&1 | tail -10
npm run lint 2>&1 | tail -10
```

Expected: No errors or warnings on new files.

- [ ] **Step 13.4: Note for manual migration**

The migration `AddArticleGenerationSteps` must be applied manually to the dev/staging database before testing end-to-end:

```bash
# Apply against dev database (run on the server or locally with the right connection string)
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/backend
dotnet ef database update \
  --project src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj \
  --startup-project src/Anela.Heblo.API/Anela.Heblo.API.csproj
```

This is outside the automated pipeline — note for the developer to run manually before starting the server.

---

## Self-Review

**Spec coverage:**
- ✅ `ArticleGenerationStep` entity with all required fields
- ✅ `ArticleGenerationStepStatus` enum
- ✅ `PipelineStepRecorder` with timing, error handling, serialization
- ✅ All 5 steps wrapped: PlanQueries, GatherContext, AggregateFacts, ValidateFacts, WriteArticle
- ✅ `GatherContextStep` captures all snippets (not just deduped)
- ✅ `AggregateFactsStep` captures summary and gaps
- ✅ `ValidateFactsStep` captures reliable flag
- ✅ Raw LLM responses captured in all 4 LLM steps
- ✅ `GetWithStepsAsync`, `AddStepAsync`, `UpdateStepAsync` in repository
- ✅ EF config: text columns for JSON, string enum conversion, composite index, cascade delete
- ✅ No explicit schema (matches Article table style, not Smartsupp)
- ✅ Migration created (not run)
- ✅ `GET /api/articles/{id}/trace` endpoint
- ✅ DTOs are classes not records (CLAUDE.md)
- ✅ `ArticleNotFound` error code used (existing 2401)
- ✅ `useArticleTrace` hook with `enabled` flag — only fires on expand
- ✅ Absolute URL pattern used in hook (`baseUrl` prefix)
- ✅ Collapsible debug panel — lazy loads on first expand
- ✅ Czech labels (`Debug — průběh generování`, `Vstup`, `Výstup`, `Běží`, `Dokončeno`, `Chyba`)
- ✅ Tests: PipelineStepRecorder (3 tests), GetArticleTraceHandler (3 tests)

**Placeholder scan:** No TBD or TODO in code steps. All code blocks are complete.

**Type consistency:** `ArticleGenerationStep`, `ArticleGenerationStepStatus`, `PipelineStepRecorder`, `ArticleGenerationStepDto`, `GetArticleTraceResponse` — names consistent across tasks 1–12.
