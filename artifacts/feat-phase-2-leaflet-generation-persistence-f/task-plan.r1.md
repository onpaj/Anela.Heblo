# Leaflet Generation Persistence + Feedback (Phase 2) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Persist every successful leaflet generation to the database and expose a one-shot, owner-only feedback flow with a manager-gated list endpoint, mirroring the proven KnowledgeBase Q&A pattern.

**Architecture:** A new `LeafletGeneration` entity persisted via a MediatR `IPipelineBehavior` after `GenerateLeafletHandler` returns successfully. Three new endpoints on `LeafletController` (`POST /api/leaflet/feedback`, `GET /api/leaflet/feedback/list`, `GET /api/leaflet/generations/{id}`). A shared `RagFeedbackForm` component is extracted from `KnowledgeBaseSearchAskTab` and consumed by both KB and Leaflet result panels.

**Tech Stack:** .NET 8 (MediatR, EF Core, PostgreSQL), React + TanStack Query, Vertical Slice organization. Branch policy: cut from `feature/genai_consistency`, target PR back to `feature/genai_consistency` (NOT `main`).

---

## Prerequisites Checklist

- [ ] Phase 1 (LeafletController.Generate, GenerateLeafletHandler, LeafletResult.tsx, sidebar, route) is merged into `feature/genai_consistency`. Verify with `git log feature/genai_consistency --grep="leaflet"`.
- [ ] Current branch `feat-phase-2-leaflet-generation-persistence-f` was cut from `feature/genai_consistency`. Verify with `git merge-base feature/genai_consistency HEAD` returns a commit on `feature/genai_consistency`.
- [ ] `dotnet ef` tool is installed. Verify with `dotnet ef --version`.
- [ ] Postgres dev DB is reachable.

---

## File Structure

**Backend new files:**
- `backend/src/Anela.Heblo.Domain/Features/Leaflet/LeafletGeneration.cs` — entity
- `backend/src/Anela.Heblo.Domain/Features/Leaflet/LeafletFeedbackStats.cs` — sealed record
- `backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletGenerationConfiguration.cs` — EF config
- `backend/src/Anela.Heblo.Persistence/Migrations/20260506*_AddLeafletGenerations.cs` — migration (auto-generated)
- `backend/src/Anela.Heblo.Application/Features/Leaflet/Pipeline/LeafletGenerationLoggingBehavior.cs` — pipeline behavior
- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/SubmitLeafletFeedback/{Request,Response,Handler}.cs`
- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletFeedbackList/{Request,Response,Handler}.cs`
- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletGeneration/{Request,Response,Handler}.cs`
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/Pipeline/LeafletGenerationLoggingBehaviorTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/SubmitLeafletFeedbackHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/GetLeafletFeedbackListHandlerTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/GetLeafletGenerationHandlerTests.cs`
- `frontend/src/components/feedback/RagFeedbackForm.tsx` — shared component
- `frontend/src/components/feedback/__tests__/RagFeedbackForm.test.tsx`

**Backend modified files:**
- `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` — add 3 codes
- `backend/src/Anela.Heblo.Domain/Features/Leaflet/ILeafletRepository.cs` — +4 methods
- `backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletRepository.cs` — +4 implementations
- `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs` — add `DbSet<LeafletGeneration>`
- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/GenerateLeafletResponse.cs` — add `Id`, `KbSourceCount`, `LeafletSourceCount`
- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/GenerateLeafletHandler.cs` — populate counts
- `backend/src/Anela.Heblo.Application/Features/Leaflet/LeafletModule.cs` — register pipeline behavior
- `backend/src/Anela.Heblo.API/Controllers/LeafletController.cs` — 3 new actions

**Frontend modified files:**
- `frontend/src/components/knowledge-base/KnowledgeBaseSearchAskTab.tsx` — use shared form
- `frontend/src/features/leaflet-generator/LeafletResult.tsx` — accept `generationId?` prop
- `frontend/src/features/leaflet-generator/LeafletGenerateTab.tsx` — pass `generationId` from response
- `frontend/src/api/hooks/useLeaflet.ts` — add types + `useSubmitLeafletFeedbackMutation`
- `frontend/src/i18n.ts` — add 3 error code translations (cs + en)

---

## Task 1: Add ErrorCodes for Leaflet feedback flow

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs:247-249`

- [ ] **Step 1: Add three new enum values to the Leaflet block (25XX)**

Replace the existing Leaflet block:

```csharp
    // Leaflet module errors (25XX)
    [HttpStatusCode(HttpStatusCode.NotFound)]
    LeafletChunkNotFound = 2501,
```

with:

```csharp
    // Leaflet module errors (25XX)
    [HttpStatusCode(HttpStatusCode.NotFound)]
    LeafletChunkNotFound = 2501,
    [HttpStatusCode(HttpStatusCode.NotFound)]
    LeafletFeedbackNotFound = 2502,
    [HttpStatusCode(HttpStatusCode.Conflict)]
    LeafletFeedbackAlreadySubmitted = 2503,
    [HttpStatusCode(HttpStatusCode.NotFound)]
    LeafletGenerationNotFound = 2504,
```

- [ ] **Step 2: Build to verify enum compiles**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs
git commit -m "feat: add Leaflet feedback error codes"
```

---

## Task 2: Add `LeafletGeneration` domain entity and `LeafletFeedbackStats` record

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Leaflet/LeafletGeneration.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/Leaflet/LeafletFeedbackStats.cs`

- [ ] **Step 1: Write `LeafletGeneration.cs`**

```csharp
namespace Anela.Heblo.Domain.Features.Leaflet;

public class LeafletGeneration
{
    public Guid Id { get; set; }
    public string Topic { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string Length { get; set; } = string.Empty;
    public string FinalMarkdown { get; set; } = string.Empty;
    public int KbSourceCount { get; set; }
    public int LeafletSourceCount { get; set; }
    public long DurationMs { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? UserId { get; set; }
    public int? PrecisionScore { get; set; }
    public int? StyleScore { get; set; }
    public string? FeedbackComment { get; set; }
}
```

- [ ] **Step 2: Write `LeafletFeedbackStats.cs`**

```csharp
namespace Anela.Heblo.Domain.Features.Leaflet;

public sealed record LeafletFeedbackStats(
    int TotalGenerations,
    int TotalWithFeedback,
    double? AvgPrecisionScore,
    double? AvgStyleScore);
```

- [ ] **Step 3: Build domain project**

Run: `dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj`
Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Leaflet/LeafletGeneration.cs \
        backend/src/Anela.Heblo.Domain/Features/Leaflet/LeafletFeedbackStats.cs
git commit -m "feat: add LeafletGeneration entity and LeafletFeedbackStats record"
```

---

## Task 3: Extend `ILeafletRepository` with 4 new methods

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Leaflet/ILeafletRepository.cs`

- [ ] **Step 1: Add four new method signatures before the closing brace**

Insert these lines just before the closing `}` of `ILeafletRepository`:

```csharp
    Task SaveGenerationAsync(LeafletGeneration generation, CancellationToken ct = default);
    Task<LeafletGeneration?> GetGenerationByIdAsync(Guid id, CancellationToken ct = default);
    Task<(IReadOnlyList<LeafletGeneration> Items, int TotalCount)> GetGenerationsPagedAsync(
        bool? hasFeedback,
        string? userId,
        string sortBy,
        bool sortDescending,
        int pageNumber,
        int pageSize,
        CancellationToken ct = default);
    Task<LeafletFeedbackStats> GetGenerationStatsAsync(CancellationToken ct = default);
```

- [ ] **Step 2: Build domain — implementation will fail (expected)**

Run: `dotnet build backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj`
Expected: FAIL — `LeafletRepository does not implement interface members SaveGenerationAsync, …`. This is the failing test for the next task.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Leaflet/ILeafletRepository.cs
git commit -m "feat: extend ILeafletRepository with generation and feedback methods"
```

---

## Task 4: Add EF configuration for `LeafletGeneration`

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletGenerationConfiguration.cs`

- [ ] **Step 1: Write the configuration**

```csharp
using Anela.Heblo.Domain.Features.Leaflet;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Features.Leaflet;

public class LeafletGenerationConfiguration : IEntityTypeConfiguration<LeafletGeneration>
{
    public void Configure(EntityTypeBuilder<LeafletGeneration> builder)
    {
        builder.ToTable("LeafletGenerations", "public");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Topic).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Audience).IsRequired().HasMaxLength(50);
        builder.Property(x => x.Length).IsRequired().HasMaxLength(50);
        builder.Property(x => x.FinalMarkdown).IsRequired().HasColumnType("text");
        builder.Property(x => x.KbSourceCount).IsRequired();
        builder.Property(x => x.LeafletSourceCount).IsRequired();
        builder.Property(x => x.DurationMs).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UserId).IsRequired(false).HasMaxLength(200);
        builder.Property(x => x.PrecisionScore).IsRequired(false);
        builder.Property(x => x.StyleScore).IsRequired(false);
        builder.Property(x => x.FeedbackComment).IsRequired(false).HasColumnType("text");

        builder.HasIndex(x => x.CreatedAt);
        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => x.PrecisionScore).HasFilter("\"PrecisionScore\" IS NOT NULL");
    }
}
```

- [ ] **Step 2: Build persistence project — still fails (interface members missing)**

Run: `dotnet build backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj`
Expected: still FAILS on `LeafletRepository` interface implementation.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletGenerationConfiguration.cs
git commit -m "feat: add EF configuration for LeafletGenerations table"
```

---

## Task 5: Add `DbSet<LeafletGeneration>` to `ApplicationDbContext`

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs:88-89`

- [ ] **Step 1: Add a new `DbSet` next to `LeafletChunks`**

Replace:

```csharp
    // Leaflet module
    public DbSet<LeafletDocument> LeafletDocuments { get; set; } = null!;
    public DbSet<LeafletChunk> LeafletChunks { get; set; } = null!;
```

with:

```csharp
    // Leaflet module
    public DbSet<LeafletDocument> LeafletDocuments { get; set; } = null!;
    public DbSet<LeafletChunk> LeafletChunks { get; set; } = null!;
    public DbSet<LeafletGeneration> LeafletGenerations { get; set; } = null!;
```

- [ ] **Step 2: Verify build still fails on missing repository methods**

Run: `dotnet build backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj`
Expected: still FAILS on `LeafletRepository` (interface members not implemented).

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs
git commit -m "feat: add LeafletGenerations DbSet"
```

---

## Task 6: Implement repository methods on `LeafletRepository`

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletRepository.cs`

- [ ] **Step 1: Add the four new methods just before the closing `}` of the class**

```csharp
    public async Task SaveGenerationAsync(LeafletGeneration generation, CancellationToken ct = default)
    {
        _context.LeafletGenerations.Add(generation);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<LeafletGeneration?> GetGenerationByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.LeafletGenerations
            .FirstOrDefaultAsync(g => g.Id == id, ct);
    }

    public async Task<(IReadOnlyList<LeafletGeneration> Items, int TotalCount)> GetGenerationsPagedAsync(
        bool? hasFeedback,
        string? userId,
        string sortBy,
        bool sortDescending,
        int pageNumber,
        int pageSize,
        CancellationToken ct = default)
    {
        var query = _context.LeafletGenerations.AsNoTracking().AsQueryable();

        if (hasFeedback.HasValue)
        {
            query = hasFeedback.Value
                ? query.Where(g => g.PrecisionScore != null || g.StyleScore != null)
                : query.Where(g => g.PrecisionScore == null && g.StyleScore == null);
        }

        if (!string.IsNullOrEmpty(userId))
            query = query.Where(g => g.UserId == userId);

        query = sortBy switch
        {
            "PrecisionScore" => sortDescending
                ? query.OrderByDescending(g => g.PrecisionScore)
                : query.OrderBy(g => g.PrecisionScore),
            "StyleScore" => sortDescending
                ? query.OrderByDescending(g => g.StyleScore)
                : query.OrderBy(g => g.StyleScore),
            _ => sortDescending
                ? query.OrderByDescending(g => g.CreatedAt)
                : query.OrderBy(g => g.CreatedAt),
        };

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    // NFR-1: prefer indexed aggregates over loading all rows into memory.
    public async Task<LeafletFeedbackStats> GetGenerationStatsAsync(CancellationToken ct = default)
    {
        var totalGenerations = await _context.LeafletGenerations.CountAsync(ct);

        var totalWithFeedback = await _context.LeafletGenerations
            .Where(g => g.PrecisionScore != null || g.StyleScore != null)
            .CountAsync(ct);

        double? avgPrecision = await _context.LeafletGenerations
            .Where(g => g.PrecisionScore != null)
            .AverageAsync(g => (double?)g.PrecisionScore, ct);

        double? avgStyle = await _context.LeafletGenerations
            .Where(g => g.StyleScore != null)
            .AverageAsync(g => (double?)g.StyleScore, ct);

        return new LeafletFeedbackStats(
            totalGenerations,
            totalWithFeedback,
            avgPrecision.HasValue ? Math.Round(avgPrecision.Value, 1) : null,
            avgStyle.HasValue ? Math.Round(avgStyle.Value, 1) : null);
    }
```

- [ ] **Step 2: Build persistence — should now succeed**

Run: `dotnet build backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletRepository.cs
git commit -m "feat: implement LeafletGeneration repository methods"
```

---

## Task 7: Generate EF migration `AddLeafletGenerations`

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/Migrations/<timestamp>_AddLeafletGenerations.cs` (auto-generated)
- Create: `backend/src/Anela.Heblo.Persistence/Migrations/<timestamp>_AddLeafletGenerations.Designer.cs` (auto-generated)
- Modify: `backend/src/Anela.Heblo.Persistence/Migrations/ApplicationDbContextModelSnapshot.cs` (auto-updated)

- [ ] **Step 1: Run the EF migration command from the repo root**

Run:
```bash
dotnet ef migrations add AddLeafletGenerations \
  --project backend/src/Anela.Heblo.Persistence \
  --startup-project backend/src/Anela.Heblo.API \
  --output-dir Migrations
```
Expected: 2 new files created in `backend/src/Anela.Heblo.Persistence/Migrations/`. Snapshot file is updated.

- [ ] **Step 2: Inspect the generated `Up()` method**

Open the new `*_AddLeafletGenerations.cs` file. Confirm:
- `schema: "public"` is set on the `CreateTable` call.
- All 13 columns are present: `Id`, `Topic`, `Audience`, `Length`, `FinalMarkdown`, `KbSourceCount`, `LeafletSourceCount`, `DurationMs`, `CreatedAt`, `UserId`, `PrecisionScore`, `StyleScore`, `FeedbackComment`.
- Three indexes are created: on `CreatedAt`, on `UserId`, on `PrecisionScore` with filter `"PrecisionScore" IS NOT NULL`.

If any of these are missing, delete both new migration files plus the snapshot diff (`git restore backend/src/Anela.Heblo.Persistence/Migrations/ApplicationDbContextModelSnapshot.cs`), fix `LeafletGenerationConfiguration.cs`, and re-run Step 1.

- [ ] **Step 3: Apply the migration to the local dev DB**

Run:
```bash
dotnet ef database update \
  --project backend/src/Anela.Heblo.Persistence \
  --startup-project backend/src/Anela.Heblo.API
```
Expected: migration applies cleanly. Verify with `psql -c '\dt public."LeafletGenerations"'` (table exists with correct columns).

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Migrations/
git commit -m "feat: add LeafletGenerations migration"
```

---

## Task 8: Extend `GenerateLeafletResponse` with Id and source counts

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/GenerateLeafletResponse.cs`

- [ ] **Step 1: Replace the file contents**

```csharp
using System.Text.Json.Serialization;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Leaflet.UseCases.GenerateLeaflet;

public class GenerateLeafletResponse : BaseResponse
{
    [JsonPropertyName("id")]
    public Guid? Id { get; set; }

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("kbSourceCount")]
    public int KbSourceCount { get; set; }

    [JsonPropertyName("leafletSourceCount")]
    public int LeafletSourceCount { get; set; }
}
```

Note: `Id`, `KbSourceCount`, `LeafletSourceCount` use `set` (not `init`) so the pipeline behavior and handler can write them after construction.

- [ ] **Step 2: Build application project**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/GenerateLeafletResponse.cs
git commit -m "feat: expose Id and source counts on GenerateLeafletResponse"
```

---

## Task 9: Populate source counts in `GenerateLeafletHandler`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/GenerateLeafletHandler.cs:134`

- [ ] **Step 1: Replace the final `return` line**

Replace:

```csharp
        return new GenerateLeafletResponse { Content = leafletResponse.Text ?? string.Empty };
```

with:

```csharp
        return new GenerateLeafletResponse
        {
            Content = leafletResponse.Text ?? string.Empty,
            KbSourceCount = kbHits.Count,
            LeafletSourceCount = leafletHits.Count,
        };
```

- [ ] **Step 2: Build application project**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/GenerateLeafletHandler.cs
git commit -m "feat: populate KbSourceCount and LeafletSourceCount in GenerateLeafletHandler"
```

---

## Task 10: Write `LeafletGenerationLoggingBehaviorTests` (TDD — RED)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Leaflet/Pipeline/LeafletGenerationLoggingBehaviorTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using Anela.Heblo.Application.Features.Leaflet.Pipeline;
using Anela.Heblo.Application.Features.Leaflet.UseCases.GenerateLeaflet;
using Anela.Heblo.Domain.Features.Leaflet;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Leaflet.Pipeline;

public class LeafletGenerationLoggingBehaviorTests
{
    private readonly Mock<ILeafletRepository> _repository = new();
    private readonly Mock<ICurrentUserService> _userService = new();
    private readonly Mock<ILogger<LeafletGenerationLoggingBehavior>> _logger = new();

    private LeafletGenerationLoggingBehavior CreateBehavior() =>
        new(_repository.Object, _userService.Object, _logger.Object);

    [Fact]
    public async Task Handle_OnSuccess_PersistsRowAndSetsResponseId()
    {
        _userService.Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser("user-1", "Test User", null, true));

        var request = new GenerateLeafletRequest
        {
            Topic = "Hyaluronic acid",
            Audience = AudienceType.EndConsumer,
            Length = LeafletLength.Medium,
        };
        var expected = new GenerateLeafletResponse
        {
            Content = "# Final markdown",
            KbSourceCount = 3,
            LeafletSourceCount = 2,
        };

        LeafletGeneration? captured = null;
        _repository
            .Setup(r => r.SaveGenerationAsync(It.IsAny<LeafletGeneration>(), It.IsAny<CancellationToken>()))
            .Callback<LeafletGeneration, CancellationToken>((g, _) => captured = g)
            .Returns(Task.CompletedTask);

        var result = await CreateBehavior().Handle(request, () => Task.FromResult(expected), default);

        result.Should().BeSameAs(expected);
        captured.Should().NotBeNull();
        captured!.Topic.Should().Be("Hyaluronic acid");
        captured.Audience.Should().Be("EndConsumer");
        captured.Length.Should().Be("Medium");
        captured.FinalMarkdown.Should().Be("# Final markdown");
        captured.KbSourceCount.Should().Be(3);
        captured.LeafletSourceCount.Should().Be(2);
        captured.UserId.Should().Be("user-1");
        captured.DurationMs.Should().BeGreaterThanOrEqualTo(0);
        captured.PrecisionScore.Should().BeNull();
        captured.StyleScore.Should().BeNull();
        result.Id.Should().Be(captured.Id);
        result.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task Handle_WhenResponseUnsuccessful_DoesNotPersist()
    {
        _userService.Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser("user-1", "Test User", null, true));

        var request = new GenerateLeafletRequest
        {
            Topic = "X", Audience = AudienceType.B2B, Length = LeafletLength.Short,
        };
        var failed = new GenerateLeafletResponse { Content = "" };
        failed.Success = false;

        var result = await CreateBehavior().Handle(request, () => Task.FromResult(failed), default);

        result.Should().BeSameAs(failed);
        result.Id.Should().BeNull();
        _repository.Verify(r => r.SaveGenerationAsync(It.IsAny<LeafletGeneration>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenSaveThrows_ResponseStillReturned()
    {
        _userService.Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser("user-1", "Test User", null, true));

        _repository
            .Setup(r => r.SaveGenerationAsync(It.IsAny<LeafletGeneration>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("DB exploded"));

        var request = new GenerateLeafletRequest
        {
            Topic = "X", Audience = AudienceType.B2B, Length = LeafletLength.Short,
        };
        var expected = new GenerateLeafletResponse { Content = "ok" };

        var result = await CreateBehavior().Handle(request, () => Task.FromResult(expected), default);

        result.Content.Should().Be("ok");
        result.Id.Should().BeNull();
    }

    [Fact]
    public async Task Handle_PassesCancellationTokenNoneToSave()
    {
        _userService.Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser("user-1", "Test User", null, true));

        CancellationToken capturedToken = default;
        _repository
            .Setup(r => r.SaveGenerationAsync(It.IsAny<LeafletGeneration>(), It.IsAny<CancellationToken>()))
            .Callback<LeafletGeneration, CancellationToken>((_, t) => capturedToken = t)
            .Returns(Task.CompletedTask);

        var cts = new CancellationTokenSource();
        cts.Cancel();

        var request = new GenerateLeafletRequest
        {
            Topic = "X", Audience = AudienceType.B2B, Length = LeafletLength.Short,
        };
        var expected = new GenerateLeafletResponse { Content = "ok" };

        await CreateBehavior().Handle(request, () => Task.FromResult(expected), cts.Token);

        capturedToken.Should().Be(CancellationToken.None);
    }
}
```

- [ ] **Step 2: Run the tests — they should FAIL (no behavior class yet)**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~LeafletGenerationLoggingBehaviorTests"`
Expected: compilation FAIL (`LeafletGenerationLoggingBehavior` not found).

---

## Task 11: Implement `LeafletGenerationLoggingBehavior` (TDD — GREEN)

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Leaflet/Pipeline/LeafletGenerationLoggingBehavior.cs`

- [ ] **Step 1: Write the behavior**

```csharp
using System.Diagnostics;
using Anela.Heblo.Application.Features.Leaflet.UseCases.GenerateLeaflet;
using Anela.Heblo.Domain.Features.Leaflet;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Leaflet.Pipeline;

public class LeafletGenerationLoggingBehavior
    : IPipelineBehavior<GenerateLeafletRequest, GenerateLeafletResponse>
{
    private readonly ILeafletRepository _repository;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<LeafletGenerationLoggingBehavior> _logger;

    public LeafletGenerationLoggingBehavior(
        ILeafletRepository repository,
        ICurrentUserService currentUserService,
        ILogger<LeafletGenerationLoggingBehavior> logger)
    {
        _repository = repository;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<GenerateLeafletResponse> Handle(
        GenerateLeafletRequest request,
        RequestHandlerDelegate<GenerateLeafletResponse> next,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        // next() runs OUTSIDE try/catch — generator failures must propagate.
        var response = await next();
        sw.Stop();

        if (!response.Success)
            return response;

        try
        {
            var currentUser = _currentUserService.GetCurrentUser();
            var generation = new LeafletGeneration
            {
                Id = Guid.NewGuid(),
                Topic = request.Topic,
                Audience = request.Audience.ToString(),
                Length = request.Length.ToString(),
                FinalMarkdown = response.Content,
                KbSourceCount = response.KbSourceCount,
                LeafletSourceCount = response.LeafletSourceCount,
                DurationMs = sw.ElapsedMilliseconds,
                CreatedAt = DateTimeOffset.UtcNow,
                UserId = currentUser.Id,
            };

            // Use CancellationToken.None: avoid losing the audit row on client disconnect.
            await _repository.SaveGenerationAsync(generation, CancellationToken.None);
            response.Id = generation.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write leaflet generation log. Topic: {Topic}", request.Topic);
        }

        return response;
    }
}
```

- [ ] **Step 2: Run the tests — they should PASS**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~LeafletGenerationLoggingBehaviorTests"`
Expected: 4 PASSED.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Leaflet/Pipeline/LeafletGenerationLoggingBehavior.cs \
        backend/test/Anela.Heblo.Tests/Features/Leaflet/Pipeline/LeafletGenerationLoggingBehaviorTests.cs
git commit -m "feat: add LeafletGenerationLoggingBehavior to persist generations"
```

---

## Task 12: Register pipeline behavior in `LeafletModule`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Leaflet/LeafletModule.cs`

- [ ] **Step 1: Add the registration**

Replace the file contents:

```csharp
using Anela.Heblo.Application.Features.Leaflet.Pipeline;
using Anela.Heblo.Application.Features.Leaflet.Services;
using Anela.Heblo.Application.Features.Leaflet.UseCases.GenerateLeaflet;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Leaflet;

public static class LeafletModule
{
    public static IServiceCollection AddLeafletModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<LeafletOptions>()
            .Bind(configuration.GetSection(LeafletOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<ILeafletChunkSummarizer, LeafletChunkSummarizer>();
        services.AddScoped<ILeafletIndexingService, LeafletIndexingService>();

        // LeafletIngestionJob is auto-discovered via IRecurringJob assembly scan in AddRecurringJobs()
        // ILeafletRepository is registered in PersistenceModule
        // MediatR handlers are auto-registered via AddApplicationServices() assembly scan

        // Persist generation results after a successful response. Scoped — same lifetime as repository.
        services.AddScoped<
            IPipelineBehavior<GenerateLeafletRequest, GenerateLeafletResponse>,
            LeafletGenerationLoggingBehavior>();

        return services;
    }
}
```

- [ ] **Step 2: Build the API project**

Run: `dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Leaflet/LeafletModule.cs
git commit -m "feat: register LeafletGenerationLoggingBehavior in module"
```

---

## Task 13: Write `SubmitLeafletFeedbackHandlerTests` (TDD — RED)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/SubmitLeafletFeedbackHandlerTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using Anela.Heblo.Application.Features.Leaflet.UseCases.SubmitLeafletFeedback;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Leaflet;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Leaflet.UseCases;

public class SubmitLeafletFeedbackHandlerTests
{
    private const string UserId = "user-1";

    private readonly Mock<ILeafletRepository> _repository = new();
    private readonly Mock<ICurrentUserService> _currentUserService = new();

    public SubmitLeafletFeedbackHandlerTests()
    {
        _currentUserService
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser(UserId, "Test User", null, true));
    }

    private SubmitLeafletFeedbackHandler CreateHandler() =>
        new(_repository.Object, _currentUserService.Object);

    [Fact]
    public async Task Handle_WhenGenerationNotFound_ReturnsNotFound()
    {
        var generationId = Guid.NewGuid();
        _repository
            .Setup(r => r.GetGenerationByIdAsync(generationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((LeafletGeneration?)null);

        var request = new SubmitLeafletFeedbackRequest
        {
            GenerationId = generationId, PrecisionScore = 4, StyleScore = 5,
        };
        var result = await CreateHandler().Handle(request, default);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.LeafletFeedbackNotFound);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenCallerIsNotOwner_ReturnsForbidden()
    {
        var generationId = Guid.NewGuid();
        _repository
            .Setup(r => r.GetGenerationByIdAsync(generationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LeafletGeneration
            {
                Id = generationId,
                Topic = "T",
                Audience = "EndConsumer",
                Length = "Short",
                FinalMarkdown = "M",
                UserId = "other-user",
            });

        var result = await CreateHandler().Handle(
            new SubmitLeafletFeedbackRequest
            {
                GenerationId = generationId, PrecisionScore = 4, StyleScore = 5,
            },
            default);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenPrecisionAlreadySet_ReturnsAlreadySubmitted()
    {
        var generationId = Guid.NewGuid();
        _repository
            .Setup(r => r.GetGenerationByIdAsync(generationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LeafletGeneration
            {
                Id = generationId,
                Topic = "T", Audience = "EndConsumer", Length = "Short", FinalMarkdown = "M",
                UserId = UserId,
                PrecisionScore = 4,
            });

        var result = await CreateHandler().Handle(
            new SubmitLeafletFeedbackRequest
            {
                GenerationId = generationId, PrecisionScore = 5, StyleScore = 5,
            },
            default);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.LeafletFeedbackAlreadySubmitted);
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenStyleAlreadySet_ReturnsAlreadySubmitted()
    {
        var generationId = Guid.NewGuid();
        _repository
            .Setup(r => r.GetGenerationByIdAsync(generationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LeafletGeneration
            {
                Id = generationId,
                Topic = "T", Audience = "EndConsumer", Length = "Short", FinalMarkdown = "M",
                UserId = UserId,
                StyleScore = 3,
            });

        var result = await CreateHandler().Handle(
            new SubmitLeafletFeedbackRequest
            {
                GenerationId = generationId, PrecisionScore = 5, StyleScore = 5,
            },
            default);

        result.ErrorCode.Should().Be(ErrorCodes.LeafletFeedbackAlreadySubmitted);
    }

    [Fact]
    public async Task Handle_WhenValid_PersistsAllFieldsAndReturnsSuccess()
    {
        var generationId = Guid.NewGuid();
        var generation = new LeafletGeneration
        {
            Id = generationId,
            Topic = "T", Audience = "EndConsumer", Length = "Short", FinalMarkdown = "M",
            UserId = UserId,
        };

        _repository
            .Setup(r => r.GetGenerationByIdAsync(generationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(generation);
        _repository
            .Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await CreateHandler().Handle(
            new SubmitLeafletFeedbackRequest
            {
                GenerationId = generationId,
                PrecisionScore = 4,
                StyleScore = 5,
                Comment = "Looks good",
            },
            default);

        result.Success.Should().BeTrue();
        generation.PrecisionScore.Should().Be(4);
        generation.StyleScore.Should().Be(5);
        generation.FeedbackComment.Should().Be("Looks good");
        _repository.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenValidWithNoComment_PersistsNullComment()
    {
        var generationId = Guid.NewGuid();
        var generation = new LeafletGeneration
        {
            Id = generationId,
            Topic = "T", Audience = "EndConsumer", Length = "Short", FinalMarkdown = "M",
            UserId = UserId,
        };

        _repository
            .Setup(r => r.GetGenerationByIdAsync(generationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(generation);

        var result = await CreateHandler().Handle(
            new SubmitLeafletFeedbackRequest
            {
                GenerationId = generationId, PrecisionScore = 3, StyleScore = 4,
            },
            default);

        result.Success.Should().BeTrue();
        generation.FeedbackComment.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run tests — they should FAIL (handler/types missing)**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~SubmitLeafletFeedbackHandlerTests"`
Expected: compilation FAIL.

---

## Task 14: Implement `SubmitLeafletFeedback` request, response, handler (TDD — GREEN)

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/SubmitLeafletFeedback/SubmitLeafletFeedbackRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/SubmitLeafletFeedback/SubmitLeafletFeedbackResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/SubmitLeafletFeedback/SubmitLeafletFeedbackHandler.cs`

- [ ] **Step 1: Write the request**

```csharp
using System.ComponentModel.DataAnnotations;
using MediatR;

namespace Anela.Heblo.Application.Features.Leaflet.UseCases.SubmitLeafletFeedback;

public class SubmitLeafletFeedbackRequest : IRequest<SubmitLeafletFeedbackResponse>
{
    public Guid GenerationId { get; set; }

    [Range(1, 5)]
    public int PrecisionScore { get; set; }

    [Range(1, 5)]
    public int StyleScore { get; set; }

    [MaxLength(1000)]
    public string? Comment { get; set; }
}
```

- [ ] **Step 2: Write the response**

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Leaflet.UseCases.SubmitLeafletFeedback;

public class SubmitLeafletFeedbackResponse : BaseResponse
{
    public SubmitLeafletFeedbackResponse() { }

    public SubmitLeafletFeedbackResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
```

- [ ] **Step 3: Write the handler**

```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Leaflet;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.Leaflet.UseCases.SubmitLeafletFeedback;

public class SubmitLeafletFeedbackHandler
    : IRequestHandler<SubmitLeafletFeedbackRequest, SubmitLeafletFeedbackResponse>
{
    private readonly ILeafletRepository _repository;
    private readonly ICurrentUserService _currentUserService;

    public SubmitLeafletFeedbackHandler(
        ILeafletRepository repository,
        ICurrentUserService currentUserService)
    {
        _repository = repository;
        _currentUserService = currentUserService;
    }

    public async Task<SubmitLeafletFeedbackResponse> Handle(
        SubmitLeafletFeedbackRequest request,
        CancellationToken cancellationToken)
    {
        var generation = await _repository.GetGenerationByIdAsync(request.GenerationId, cancellationToken);
        if (generation is null)
        {
            return new SubmitLeafletFeedbackResponse(
                ErrorCodes.LeafletFeedbackNotFound,
                new Dictionary<string, string> { { "generationId", request.GenerationId.ToString() } });
        }

        // Owner-only check (no policy/middleware — must load entity first to know the owner).
        var currentUser = _currentUserService.GetCurrentUser();
        if (generation.UserId != currentUser.Id)
        {
            return new SubmitLeafletFeedbackResponse(
                ErrorCodes.Forbidden,
                new Dictionary<string, string> { { "generationId", request.GenerationId.ToString() } });
        }

        if (generation.PrecisionScore is not null || generation.StyleScore is not null)
        {
            return new SubmitLeafletFeedbackResponse(
                ErrorCodes.LeafletFeedbackAlreadySubmitted,
                new Dictionary<string, string> { { "generationId", request.GenerationId.ToString() } });
        }

        generation.PrecisionScore = request.PrecisionScore;
        generation.StyleScore = request.StyleScore;
        generation.FeedbackComment = request.Comment;

        await _repository.SaveChangesAsync(cancellationToken);

        return new SubmitLeafletFeedbackResponse();
    }
}
```

- [ ] **Step 4: Run the tests — they should PASS**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~SubmitLeafletFeedbackHandlerTests"`
Expected: 6 PASSED.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/SubmitLeafletFeedback/ \
        backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/SubmitLeafletFeedbackHandlerTests.cs
git commit -m "feat: add SubmitLeafletFeedback use case"
```

---

## Task 15: Write `GetLeafletFeedbackListHandlerTests` (TDD — RED)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/GetLeafletFeedbackListHandlerTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletFeedbackList;
using Anela.Heblo.Domain.Features.Leaflet;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Leaflet.UseCases;

public class GetLeafletFeedbackListHandlerTests
{
    private readonly Mock<ILeafletRepository> _repository = new();

    private static LeafletGeneration MakeGeneration(bool hasFeedback = false) => new()
    {
        Id = Guid.NewGuid(),
        Topic = "Hyaluronic acid",
        Audience = "EndConsumer",
        Length = "Medium",
        FinalMarkdown = "# leaflet",
        KbSourceCount = 4,
        LeafletSourceCount = 2,
        DurationMs = 1234,
        CreatedAt = DateTimeOffset.UtcNow,
        UserId = "user-1",
        PrecisionScore = hasFeedback ? 4 : null,
        StyleScore = hasFeedback ? 5 : null,
        FeedbackComment = hasFeedback ? "comment" : null,
    };

    private static LeafletFeedbackStats DefaultStats() => new(10, 6, 4.2, 4.5);

    private void SetupRepository(IReadOnlyList<LeafletGeneration> rows, int? total = null, LeafletFeedbackStats? stats = null)
    {
        _repository
            .Setup(r => r.GetGenerationsPagedAsync(
                It.IsAny<bool?>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((rows, total ?? rows.Count));

        _repository
            .Setup(r => r.GetGenerationStatsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(stats ?? DefaultStats());
    }

    [Fact]
    public async Task Handle_MapsRowsToSummaries()
    {
        var row = MakeGeneration(hasFeedback: true);
        SetupRepository(new[] { row });

        var handler = new GetLeafletFeedbackListHandler(_repository.Object);
        var result = await handler.Handle(new GetLeafletFeedbackListRequest(), default);

        result.Success.Should().BeTrue();
        result.Logs.Should().HaveCount(1);
        var dto = result.Logs[0];
        dto.Id.Should().Be(row.Id);
        dto.Topic.Should().Be(row.Topic);
        dto.Audience.Should().Be(row.Audience);
        dto.Length.Should().Be(row.Length);
        dto.FinalMarkdown.Should().Be(row.FinalMarkdown);
        dto.KbSourceCount.Should().Be(row.KbSourceCount);
        dto.LeafletSourceCount.Should().Be(row.LeafletSourceCount);
        dto.DurationMs.Should().Be(row.DurationMs);
        dto.UserId.Should().Be(row.UserId);
        dto.PrecisionScore.Should().Be(4);
        dto.StyleScore.Should().Be(5);
        dto.FeedbackComment.Should().Be("comment");
        dto.HasFeedback.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ReturnsStats()
    {
        var stats = new LeafletFeedbackStats(42, 17, 3.7, 4.1);
        SetupRepository(Array.Empty<LeafletGeneration>(), stats: stats);

        var handler = new GetLeafletFeedbackListHandler(_repository.Object);
        var result = await handler.Handle(new GetLeafletFeedbackListRequest(), default);

        result.Stats.TotalGenerations.Should().Be(42);
        result.Stats.TotalWithFeedback.Should().Be(17);
        result.Stats.AvgPrecisionScore.Should().Be(3.7);
        result.Stats.AvgStyleScore.Should().Be(4.1);
    }

    [Theory]
    [InlineData(15, 20)]
    [InlineData(0, 20)]
    [InlineData(-5, 20)]
    [InlineData(10, 10)]
    [InlineData(50, 50)]
    public async Task Handle_NormalizesPaging(int requestedSize, int expectedSize)
    {
        SetupRepository(Array.Empty<LeafletGeneration>());

        var handler = new GetLeafletFeedbackListHandler(_repository.Object);
        var result = await handler.Handle(
            new GetLeafletFeedbackListRequest { PageSize = requestedSize, PageNumber = 0 },
            default);

        result.PageSize.Should().Be(expectedSize);
        result.PageNumber.Should().Be(1);
    }

    [Fact]
    public async Task Handle_NormalizesInvalidSortBy()
    {
        SetupRepository(Array.Empty<LeafletGeneration>());
        string capturedSortBy = "";
        _repository
            .Setup(r => r.GetGenerationsPagedAsync(
                It.IsAny<bool?>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<bool?, string?, string, bool, int, int, CancellationToken>(
                (_, _, sb, _, _, _, _) => capturedSortBy = sb)
            .ReturnsAsync((Array.Empty<LeafletGeneration>(), 0));

        var handler = new GetLeafletFeedbackListHandler(_repository.Object);
        await handler.Handle(new GetLeafletFeedbackListRequest { SortBy = "EvilField" }, default);

        capturedSortBy.Should().Be("CreatedAt");
    }
}
```

- [ ] **Step 2: Run tests — should FAIL (types missing)**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetLeafletFeedbackListHandlerTests"`
Expected: compilation FAIL.

---

## Task 16: Implement `GetLeafletFeedbackList` request/response/handler (TDD — GREEN)

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletFeedbackList/GetLeafletFeedbackListRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletFeedbackList/GetLeafletFeedbackListResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletFeedbackList/GetLeafletFeedbackListHandler.cs`

- [ ] **Step 1: Write the request and DTOs in the response file**

`GetLeafletFeedbackListRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletFeedbackList;

public class GetLeafletFeedbackListRequest : IRequest<GetLeafletFeedbackListResponse>
{
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string SortBy { get; set; } = "CreatedAt";
    public bool SortDescending { get; set; } = true;
    public bool? HasFeedback { get; set; }
    public string? UserId { get; set; }
}
```

`GetLeafletFeedbackListResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletFeedbackList;

public class GetLeafletFeedbackListResponse : BaseResponse
{
    public List<LeafletFeedbackSummary> Logs { get; set; } = [];
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;
    public LeafletFeedbackStatsDto Stats { get; set; } = new();
}

public class LeafletFeedbackSummary
{
    public Guid Id { get; set; }
    public string Topic { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string Length { get; set; } = string.Empty;
    public string FinalMarkdown { get; set; } = string.Empty;
    public int KbSourceCount { get; set; }
    public int LeafletSourceCount { get; set; }
    public long DurationMs { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? UserId { get; set; }
    public int? PrecisionScore { get; set; }
    public int? StyleScore { get; set; }
    public string? FeedbackComment { get; set; }
    public bool HasFeedback => PrecisionScore.HasValue || StyleScore.HasValue;
}

public class LeafletFeedbackStatsDto
{
    public int TotalGenerations { get; set; }
    public int TotalWithFeedback { get; set; }
    public double? AvgPrecisionScore { get; set; }
    public double? AvgStyleScore { get; set; }
}
```

- [ ] **Step 2: Write the handler**

```csharp
using Anela.Heblo.Domain.Features.Leaflet;
using MediatR;

namespace Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletFeedbackList;

public class GetLeafletFeedbackListHandler
    : IRequestHandler<GetLeafletFeedbackListRequest, GetLeafletFeedbackListResponse>
{
    private static readonly int[] AllowedPageSizes = [10, 20, 50];
    private static readonly string[] AllowedSortColumns = ["CreatedAt", "PrecisionScore", "StyleScore"];

    private readonly ILeafletRepository _repository;

    public GetLeafletFeedbackListHandler(ILeafletRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetLeafletFeedbackListResponse> Handle(
        GetLeafletFeedbackListRequest request,
        CancellationToken cancellationToken)
    {
        var pageNumber = Math.Max(1, request.PageNumber);
        var pageSize = AllowedPageSizes.Contains(request.PageSize) ? request.PageSize : 20;
        var sortBy = AllowedSortColumns.Contains(request.SortBy) ? request.SortBy : "CreatedAt";

        var (rows, totalCount) = await _repository.GetGenerationsPagedAsync(
            request.HasFeedback,
            request.UserId,
            sortBy,
            request.SortDescending,
            pageNumber,
            pageSize,
            cancellationToken);

        var stats = await _repository.GetGenerationStatsAsync(cancellationToken);

        return new GetLeafletFeedbackListResponse
        {
            Logs = rows.Select(g => new LeafletFeedbackSummary
            {
                Id = g.Id,
                Topic = g.Topic,
                Audience = g.Audience,
                Length = g.Length,
                FinalMarkdown = g.FinalMarkdown,
                KbSourceCount = g.KbSourceCount,
                LeafletSourceCount = g.LeafletSourceCount,
                DurationMs = g.DurationMs,
                CreatedAt = g.CreatedAt,
                UserId = g.UserId,
                PrecisionScore = g.PrecisionScore,
                StyleScore = g.StyleScore,
                FeedbackComment = g.FeedbackComment,
            }).ToList(),
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize,
            Stats = new LeafletFeedbackStatsDto
            {
                TotalGenerations = stats.TotalGenerations,
                TotalWithFeedback = stats.TotalWithFeedback,
                AvgPrecisionScore = stats.AvgPrecisionScore,
                AvgStyleScore = stats.AvgStyleScore,
            },
        };
    }
}
```

- [ ] **Step 3: Run tests — should PASS**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetLeafletFeedbackListHandlerTests"`
Expected: 8 PASSED.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletFeedbackList/ \
        backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/GetLeafletFeedbackListHandlerTests.cs
git commit -m "feat: add GetLeafletFeedbackList use case"
```

---

## Task 17: Write `GetLeafletGenerationHandlerTests` (TDD — RED)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/GetLeafletGenerationHandlerTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletGeneration;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Leaflet;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Leaflet.UseCases;

public class GetLeafletGenerationHandlerTests
{
    private readonly Mock<ILeafletRepository> _repository = new();

    private GetLeafletGenerationHandler CreateHandler() => new(_repository.Object);

    [Fact]
    public async Task Handle_WhenNotFound_ReturnsNotFound()
    {
        var id = Guid.NewGuid();
        _repository
            .Setup(r => r.GetGenerationByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((LeafletGeneration?)null);

        var result = await CreateHandler().Handle(
            new GetLeafletGenerationRequest { Id = id }, default);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.LeafletGenerationNotFound);
    }

    [Fact]
    public async Task Handle_WhenFound_ReturnsFullPayload()
    {
        var id = Guid.NewGuid();
        var row = new LeafletGeneration
        {
            Id = id,
            Topic = "Aloe Vera",
            Audience = "B2B",
            Length = "Long",
            FinalMarkdown = "# m",
            KbSourceCount = 5,
            LeafletSourceCount = 3,
            DurationMs = 9999,
            CreatedAt = DateTimeOffset.UtcNow,
            UserId = "user-7",
            PrecisionScore = 4,
            StyleScore = 5,
            FeedbackComment = "good",
        };

        _repository
            .Setup(r => r.GetGenerationByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(row);

        var result = await CreateHandler().Handle(
            new GetLeafletGenerationRequest { Id = id }, default);

        result.Success.Should().BeTrue();
        result.Id.Should().Be(id);
        result.Topic.Should().Be("Aloe Vera");
        result.Audience.Should().Be("B2B");
        result.Length.Should().Be("Long");
        result.FinalMarkdown.Should().Be("# m");
        result.KbSourceCount.Should().Be(5);
        result.LeafletSourceCount.Should().Be(3);
        result.DurationMs.Should().Be(9999);
        result.UserId.Should().Be("user-7");
        result.PrecisionScore.Should().Be(4);
        result.StyleScore.Should().Be(5);
        result.FeedbackComment.Should().Be("good");
    }
}
```

- [ ] **Step 2: Run tests — should FAIL**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetLeafletGenerationHandlerTests"`
Expected: compilation FAIL.

---

## Task 18: Implement `GetLeafletGeneration` request/response/handler (TDD — GREEN)

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletGeneration/GetLeafletGenerationRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletGeneration/GetLeafletGenerationResponse.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletGeneration/GetLeafletGenerationHandler.cs`

- [ ] **Step 1: Write the request**

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletGeneration;

public class GetLeafletGenerationRequest : IRequest<GetLeafletGenerationResponse>
{
    public Guid Id { get; set; }
}
```

- [ ] **Step 2: Write the response**

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletGeneration;

public class GetLeafletGenerationResponse : BaseResponse
{
    public GetLeafletGenerationResponse() { }

    public GetLeafletGenerationResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }

    public Guid Id { get; set; }
    public string Topic { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string Length { get; set; } = string.Empty;
    public string FinalMarkdown { get; set; } = string.Empty;
    public int KbSourceCount { get; set; }
    public int LeafletSourceCount { get; set; }
    public long DurationMs { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? UserId { get; set; }
    public int? PrecisionScore { get; set; }
    public int? StyleScore { get; set; }
    public string? FeedbackComment { get; set; }
}
```

- [ ] **Step 3: Write the handler**

```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Leaflet;
using MediatR;

namespace Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletGeneration;

public class GetLeafletGenerationHandler
    : IRequestHandler<GetLeafletGenerationRequest, GetLeafletGenerationResponse>
{
    private readonly ILeafletRepository _repository;

    public GetLeafletGenerationHandler(ILeafletRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetLeafletGenerationResponse> Handle(
        GetLeafletGenerationRequest request,
        CancellationToken cancellationToken)
    {
        var generation = await _repository.GetGenerationByIdAsync(request.Id, cancellationToken);
        if (generation is null)
        {
            return new GetLeafletGenerationResponse(
                ErrorCodes.LeafletGenerationNotFound,
                new Dictionary<string, string> { { "id", request.Id.ToString() } });
        }

        return new GetLeafletGenerationResponse
        {
            Id = generation.Id,
            Topic = generation.Topic,
            Audience = generation.Audience,
            Length = generation.Length,
            FinalMarkdown = generation.FinalMarkdown,
            KbSourceCount = generation.KbSourceCount,
            LeafletSourceCount = generation.LeafletSourceCount,
            DurationMs = generation.DurationMs,
            CreatedAt = generation.CreatedAt,
            UserId = generation.UserId,
            PrecisionScore = generation.PrecisionScore,
            StyleScore = generation.StyleScore,
            FeedbackComment = generation.FeedbackComment,
        };
    }
}
```

- [ ] **Step 4: Run tests — should PASS**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetLeafletGenerationHandlerTests"`
Expected: 2 PASSED.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletGeneration/ \
        backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/GetLeafletGenerationHandlerTests.cs
git commit -m "feat: add GetLeafletGeneration use case"
```

---

## Task 19: Wire the three new endpoints into `LeafletController`

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/LeafletController.cs`

- [ ] **Step 1: Add the new `using` directives at the top of the file**

After the existing `using Anela.Heblo.Application.Features.Leaflet.UseCases.UploadLeaflet;` line, add:

```csharp
using Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletFeedbackList;
using Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletGeneration;
using Anela.Heblo.Application.Features.Leaflet.UseCases.SubmitLeafletFeedback;
```

- [ ] **Step 2: Append three new actions just before the closing `}` of `LeafletController`**

```csharp
    [HttpPost("feedback")]
    public async Task<ActionResult<SubmitLeafletFeedbackResponse>> SubmitFeedback(
        [FromBody] SubmitLeafletFeedbackRequest request,
        CancellationToken ct)
    {
        var result = await _mediator.Send(request, ct);
        return HandleResponse(result);
    }

    [HttpGet("feedback/list")]
    [Authorize(Policy = AuthorizationConstants.Policies.LeafletUpload)]
    public async Task<ActionResult<GetLeafletFeedbackListResponse>> GetFeedbackList(
        [FromQuery] bool? hasFeedback = null,
        [FromQuery] string? userId = null,
        [FromQuery] string sortBy = "CreatedAt",
        [FromQuery] bool sortDescending = true,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetLeafletFeedbackListRequest
        {
            HasFeedback = hasFeedback,
            UserId = userId,
            SortBy = sortBy,
            SortDescending = sortDescending,
            PageNumber = pageNumber,
            PageSize = pageSize,
        }, ct);
        return HandleResponse(result);
    }

    [HttpGet("generations/{id:guid}")]
    public async Task<ActionResult<GetLeafletGenerationResponse>> GetGeneration(
        Guid id,
        CancellationToken ct)
    {
        var result = await _mediator.Send(new GetLeafletGenerationRequest { Id = id }, ct);
        return HandleResponse(result);
    }
```

- [ ] **Step 3: Build the API project**

Run: `dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`
Expected: build succeeds.

- [ ] **Step 4: Format and run all backend tests**

Run: `dotnet format backend/Anela.Heblo.sln && dotnet test backend/Anela.Heblo.sln --no-build`
Expected: format clean, all tests pass.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/LeafletController.cs
git commit -m "feat: add leaflet feedback and generation endpoints"
```

---

## Task 20: Regenerate the OpenAPI TypeScript client

**Files:**
- Modify: `frontend/src/api/generated/api-client.ts` (auto-generated)

- [ ] **Step 1: Build the API to trigger client regeneration**

Run: `dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`

The build target regenerates `frontend/src/api/generated/api-client.ts`. Verify with:

Run: `grep -n "leaflet_SubmitFeedback\|leaflet_GetFeedbackList\|leaflet_GetGeneration\|kbSourceCount\|leafletSourceCount" frontend/src/api/generated/api-client.ts | head -20`
Expected: at least one match for each new method/property.

- [ ] **Step 2: Type-check the frontend**

Run: `cd frontend && npm run build`
Expected: no errors related to `LeafletResult` (until we update its consumer in Task 25). If any other module breaks because of the `Id`/source-count addition, fix the call site (the new fields are optional/nullable so existing callers should still compile).

- [ ] **Step 3: Commit the regenerated client**

```bash
git add frontend/src/api/generated/api-client.ts
git commit -m "chore: regenerate OpenAPI client for leaflet feedback"
```

---

## Task 21: Write `RagFeedbackForm` component tests (TDD — RED)

**Files:**
- Create: `frontend/src/components/feedback/__tests__/RagFeedbackForm.test.tsx`

- [ ] **Step 1: Write the failing tests**

```tsx
import { render, screen, fireEvent } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import RagFeedbackForm from '../RagFeedbackForm';

describe('RagFeedbackForm', () => {
  it('disables submit until both ratings are picked', () => {
    render(
      <RagFeedbackForm onSubmit={jest.fn()} isSubmitting={false} alreadySubmitted={false} isSuccess={false} />,
    );

    const submit = screen.getByRole('button', { name: /odeslat zpětnou vazbu/i });
    expect(submit).toBeDisabled();
  });

  it('enables submit after picking precision and style', async () => {
    render(
      <RagFeedbackForm onSubmit={jest.fn()} isSubmitting={false} alreadySubmitted={false} isSuccess={false} />,
    );

    fireEvent.click(screen.getAllByRole('radio', { name: '4' })[0]);
    fireEvent.click(screen.getAllByRole('radio', { name: '5' })[1]);

    const submit = screen.getByRole('button', { name: /odeslat zpětnou vazbu/i });
    expect(submit).toBeEnabled();
  });

  it('invokes onSubmit with the picked scores and trimmed comment', async () => {
    const onSubmit = jest.fn();
    render(
      <RagFeedbackForm onSubmit={onSubmit} isSubmitting={false} alreadySubmitted={false} isSuccess={false} />,
    );

    fireEvent.click(screen.getAllByRole('radio', { name: '3' })[0]);
    fireEvent.click(screen.getAllByRole('radio', { name: '5' })[1]);
    await userEvent.type(screen.getByPlaceholderText(/volitelný komentář/i), '  hello  ');

    fireEvent.click(screen.getByRole('button', { name: /odeslat zpětnou vazbu/i }));

    expect(onSubmit).toHaveBeenCalledWith({ precisionScore: 3, styleScore: 5, comment: 'hello' });
  });

  it('passes undefined comment when textarea is blank', () => {
    const onSubmit = jest.fn();
    render(
      <RagFeedbackForm onSubmit={onSubmit} isSubmitting={false} alreadySubmitted={false} isSuccess={false} />,
    );

    fireEvent.click(screen.getAllByRole('radio', { name: '2' })[0]);
    fireEvent.click(screen.getAllByRole('radio', { name: '4' })[1]);
    fireEvent.click(screen.getByRole('button', { name: /odeslat zpětnou vazbu/i }));

    expect(onSubmit).toHaveBeenCalledWith({ precisionScore: 2, styleScore: 4, comment: undefined });
  });

  it('renders the success state when isSuccess is true', () => {
    render(
      <RagFeedbackForm onSubmit={jest.fn()} isSubmitting={false} alreadySubmitted={false} isSuccess={true} />,
    );

    expect(screen.getByText(/děkujeme za vaši zpětnou vazbu/i)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /odeslat zpětnou vazbu/i })).not.toBeInTheDocument();
  });

  it('renders the alreadySubmitted state', () => {
    render(
      <RagFeedbackForm onSubmit={jest.fn()} isSubmitting={false} alreadySubmitted={true} isSuccess={false} />,
    );

    expect(screen.getByText(/zpětná vazba již byla odeslána/i)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /odeslat zpětnou vazbu/i })).not.toBeInTheDocument();
  });

  it('disables submit while submitting', () => {
    render(
      <RagFeedbackForm onSubmit={jest.fn()} isSubmitting={true} alreadySubmitted={false} isSuccess={false} />,
    );

    fireEvent.click(screen.getAllByRole('radio', { name: '3' })[0]);
    fireEvent.click(screen.getAllByRole('radio', { name: '4' })[1]);

    expect(screen.getByRole('button', { name: /odeslat zpětnou vazbu/i })).toBeDisabled();
  });
});
```

- [ ] **Step 2: Run the tests — they should FAIL (no component yet)**

Run: `cd frontend && npx jest src/components/feedback/__tests__/RagFeedbackForm.test.tsx`
Expected: module-not-found error.

---

## Task 22: Implement `RagFeedbackForm` (TDD — GREEN)

**Files:**
- Create: `frontend/src/components/feedback/RagFeedbackForm.tsx`

- [ ] **Step 1: Write the component**

```tsx
import React, { useState } from 'react';

const SCORES = [1, 2, 3, 4, 5];

interface ScoreRowProps {
  label: string;
  value: number | null;
  onChange: (value: number) => void;
}

function ScoreRow({ label, value, onChange }: ScoreRowProps) {
  return (
    <div className="space-y-1">
      <span className="text-sm font-medium text-gray-700">{label}</span>
      <div className="flex gap-1 flex-wrap">
        {SCORES.map((s) => (
          <label key={s} className="cursor-pointer">
            <input
              type="radio"
              name={label}
              value={s}
              checked={value === s}
              onChange={() => onChange(s)}
              className="sr-only"
              aria-label={String(s)}
            />
            <span
              className={`inline-flex items-center justify-center w-8 h-8 rounded text-sm font-medium border ${
                value === s
                  ? 'bg-blue-600 text-white border-blue-600'
                  : 'bg-white text-gray-700 border-gray-300 hover:bg-gray-50'
              }`}
            >
              {s}
            </span>
          </label>
        ))}
      </div>
    </div>
  );
}

export interface RagFeedbackSubmission {
  precisionScore: number;
  styleScore: number;
  comment?: string;
}

export interface RagFeedbackFormProps {
  onSubmit: (submission: RagFeedbackSubmission) => void;
  isSubmitting: boolean;
  alreadySubmitted: boolean;
  isSuccess: boolean;
}

export default function RagFeedbackForm({
  onSubmit,
  isSubmitting,
  alreadySubmitted,
  isSuccess,
}: RagFeedbackFormProps) {
  const [precisionScore, setPrecisionScore] = useState<number | null>(null);
  const [styleScore, setStyleScore] = useState<number | null>(null);
  const [comment, setComment] = useState('');

  if (isSuccess) {
    return (
      <div className="border border-gray-200 rounded-lg p-4 text-sm text-green-700 bg-green-50">
        Děkujeme za vaši zpětnou vazbu.
      </div>
    );
  }

  if (alreadySubmitted) {
    return (
      <div className="border border-gray-200 rounded-lg p-4 text-sm text-gray-600 bg-gray-50">
        Zpětná vazba již byla odeslána.
      </div>
    );
  }

  const canSubmit = precisionScore !== null && styleScore !== null && !isSubmitting;

  const handleClick = () => {
    if (precisionScore === null || styleScore === null) return;
    const trimmed = comment.trim();
    onSubmit({
      precisionScore,
      styleScore,
      comment: trimmed.length > 0 ? trimmed : undefined,
    });
  };

  return (
    <div className="border border-gray-200 rounded-lg p-4 space-y-3">
      <p className="text-sm font-medium text-gray-700">Ohodnoťte odpověď</p>
      <ScoreRow label="Přesnost" value={precisionScore} onChange={setPrecisionScore} />
      <ScoreRow label="Styl" value={styleScore} onChange={setStyleScore} />
      <textarea
        value={comment}
        onChange={(e) => setComment(e.target.value)}
        placeholder="Volitelný komentář..."
        rows={2}
        maxLength={1000}
        className="w-full border border-gray-300 rounded-lg px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-blue-500 resize-none"
      />
      <button
        type="button"
        onClick={handleClick}
        disabled={!canSubmit}
        className="px-4 py-2 bg-blue-600 text-white text-sm rounded-lg hover:bg-blue-700 disabled:opacity-50"
      >
        Odeslat zpětnou vazbu
      </button>
    </div>
  );
}
```

- [ ] **Step 2: Run tests — should PASS**

Run: `cd frontend && npx jest src/components/feedback/__tests__/RagFeedbackForm.test.tsx`
Expected: 7 PASSED.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/feedback/RagFeedbackForm.tsx \
        frontend/src/components/feedback/__tests__/RagFeedbackForm.test.tsx
git commit -m "feat: extract shared RagFeedbackForm component"
```

---

## Task 23: Refactor `KnowledgeBaseSearchAskTab` to consume `RagFeedbackForm`

**Files:**
- Modify: `frontend/src/components/knowledge-base/KnowledgeBaseSearchAskTab.tsx`

- [ ] **Step 1: Replace the inline `FeedbackForm` (lines 91–163) and the call site at line 231**

Replace the entire region from `const SCORES = [1, 2, 3, 4, 5];` (line 56) through the closing of `FeedbackForm` (line 163) with a single import-driven adapter. The simplest path: rewrite the file. Open `KnowledgeBaseSearchAskTab.tsx` and:

a. Remove the inline `SCORES`, `ScoreRow`, `FeedbackState`, and `FeedbackForm` declarations (lines 56–163).

b. Add `import RagFeedbackForm from '../feedback/RagFeedbackForm';` near the other imports at the top.

c. Add a new local adapter component above `KnowledgeBaseSearchAskTab` (or inline it in the consumer):

```tsx
interface KnowledgeBaseFeedbackAdapterProps {
  logId: string;
}

function KnowledgeBaseFeedbackAdapter({ logId }: KnowledgeBaseFeedbackAdapterProps) {
  const submitFeedback = useSubmitFeedbackMutation();
  const [alreadySubmitted, setAlreadySubmitted] = useState(false);
  const [isSuccess, setIsSuccess] = useState(false);

  const handleSubmit = ({
    precisionScore,
    styleScore,
    comment,
  }: {
    precisionScore: number;
    styleScore: number;
    comment?: string;
  }) => {
    submitFeedback.mutate(
      { logId, precisionScore, styleScore, comment },
      {
        onSuccess: (result) => {
          if (result.alreadySubmitted) setAlreadySubmitted(true);
          else setIsSuccess(true);
        },
      },
    );
  };

  return (
    <RagFeedbackForm
      onSubmit={handleSubmit}
      isSubmitting={submitFeedback.isPending}
      alreadySubmitted={alreadySubmitted}
      isSuccess={isSuccess}
    />
  );
}
```

d. Replace the existing call site `{ask.data.id && <FeedbackForm logId={ask.data.id} />}` (line 231) with:

```tsx
{ask.data.id && <KnowledgeBaseFeedbackAdapter logId={ask.data.id} />}
```

- [ ] **Step 2: Run KB-related tests + the new form tests**

Run: `cd frontend && npx jest src/components/feedback src/components/knowledge-base`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/knowledge-base/KnowledgeBaseSearchAskTab.tsx
git commit -m "refactor: KB search tab uses shared RagFeedbackForm"
```

---

## Task 24: Add `useSubmitLeafletFeedbackMutation` hook + types

**Files:**
- Modify: `frontend/src/api/hooks/useLeaflet.ts`

- [ ] **Step 1: Append types and hook to `useLeaflet.ts`**

Append to the end of the file:

```typescript
// ---- Feedback types ----

export interface SubmitLeafletFeedbackRequest {
  generationId: string;
  precisionScore: number;
  styleScore: number;
  comment?: string;
}

export interface SubmitLeafletFeedbackResult {
  alreadySubmitted?: true;
}

// ---- Hooks ----

/**
 * Submit precision and style feedback for a leaflet generation.
 * Returns { alreadySubmitted: true } on HTTP 409 instead of throwing.
 */
export const useSubmitLeafletFeedbackMutation = () => {
  return useMutation({
    mutationFn: async (
      payload: SubmitLeafletFeedbackRequest,
    ): Promise<SubmitLeafletFeedbackResult> => {
      const apiClient = getAuthenticatedApiClient();
      const fullUrl = `${(apiClient as any).baseUrl}/api/leaflet/feedback`;

      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
        body: JSON.stringify(payload),
      });

      if (response.status === 409) {
        return { alreadySubmitted: true };
      }

      if (!response.ok) {
        throw new Error(`Submit leaflet feedback failed: ${response.status}`);
      }

      return {};
    },
  });
};
```

Do NOT add `useLeafletFeedbackListQuery` — per the architecture review, this hook is YAGNI for Phase 2 (no consumer ships in this phase). It can be added when an admin dashboard is introduced in a later phase.

- [ ] **Step 2: Type-check**

Run: `cd frontend && npx tsc --noEmit`
Expected: clean.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/api/hooks/useLeaflet.ts
git commit -m "feat: add useSubmitLeafletFeedbackMutation hook"
```

---

## Task 25: Update `LeafletResult` to render `RagFeedbackForm` when `generationId` is set

**Files:**
- Modify: `frontend/src/features/leaflet-generator/LeafletResult.tsx`

- [ ] **Step 1: Replace the file contents**

```tsx
import { useState, useRef, useEffect } from 'react';
import ReactMarkdown from 'react-markdown';
import RagFeedbackForm from '../../components/feedback/RagFeedbackForm';
import { useSubmitLeafletFeedbackMutation } from '../../api/hooks/useLeaflet';

interface LeafletResultProps {
  content: string;
  onRegenerate: () => void;
  generationId?: string;
}

export default function LeafletResult({ content, onRegenerate, generationId }: LeafletResultProps) {
  const [copied, setCopied] = useState(false);
  const [alreadySubmitted, setAlreadySubmitted] = useState(false);
  const [isSuccess, setIsSuccess] = useState(false);
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const submitFeedback = useSubmitLeafletFeedbackMutation();

  useEffect(() => {
    return () => {
      if (timerRef.current) clearTimeout(timerRef.current);
    };
  }, []);

  // Reset feedback UI whenever the underlying generation changes.
  useEffect(() => {
    setAlreadySubmitted(false);
    setIsSuccess(false);
  }, [generationId]);

  if (!content) return null;

  const handleCopy = async () => {
    try {
      await navigator.clipboard.writeText(content);
      if (timerRef.current) clearTimeout(timerRef.current);
      setCopied(true);
      timerRef.current = setTimeout(() => setCopied(false), 2000);
    } catch {
      // clipboard unavailable — no feedback change
    }
  };

  const handleFeedbackSubmit = ({
    precisionScore,
    styleScore,
    comment,
  }: {
    precisionScore: number;
    styleScore: number;
    comment?: string;
  }) => {
    if (!generationId) return;
    submitFeedback.mutate(
      { generationId, precisionScore, styleScore, comment },
      {
        onSuccess: (result) => {
          if (result.alreadySubmitted) setAlreadySubmitted(true);
          else setIsSuccess(true);
        },
      },
    );
  };

  return (
    <div className="space-y-4">
      <div className="prose max-w-none">
        <ReactMarkdown>{content}</ReactMarkdown>
      </div>
      <div className="flex gap-2">
        <button
          type="button"
          onClick={handleCopy}
          className="px-4 py-2 text-sm font-medium border border-gray-300 rounded-md hover:bg-gray-50"
        >
          {copied ? 'Zkopírováno' : 'Kopírovat'}
        </button>
        <button
          type="button"
          onClick={onRegenerate}
          className="px-4 py-2 text-sm font-medium border border-gray-300 rounded-md hover:bg-gray-50"
        >
          Generovat znovu
        </button>
      </div>
      {generationId && (
        <RagFeedbackForm
          onSubmit={handleFeedbackSubmit}
          isSubmitting={submitFeedback.isPending}
          alreadySubmitted={alreadySubmitted}
          isSuccess={isSuccess}
        />
      )}
    </div>
  );
}
```

- [ ] **Step 2: Type-check**

Run: `cd frontend && npx tsc --noEmit`
Expected: clean.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/features/leaflet-generator/LeafletResult.tsx
git commit -m "feat: render feedback form below leaflet result when generationId is present"
```

---

## Task 26: Update `LeafletGenerateTab` to capture `generationId` from response

**Files:**
- Modify: `frontend/src/features/leaflet-generator/LeafletGenerateTab.tsx`

- [ ] **Step 1: Add a `generationId` state and pass it to `LeafletResult`**

Replace the file contents:

```tsx
import React, { useState } from 'react';
import LeafletForm from './LeafletForm';
import LeafletResult from './LeafletResult';
import { getAuthenticatedApiClient } from '../../api/client';
import { AudienceType, GenerateLeafletRequest, LeafletLength } from '../../api/generated/api-client';

interface ErrorBanner {
  kind: 'insufficient' | 'transient';
  message: string;
}

interface ApiError {
  status: number;
  detail?: string;
}

function isApiError(err: unknown): err is ApiError {
  return typeof err === 'object' && err !== null && typeof (err as Record<string, unknown>)['status'] === 'number';
}

const LeafletGenerateTab: React.FC = () => {
  const [topic, setTopic] = useState('');
  const [audience, setAudience] = useState<AudienceType>(AudienceType.EndConsumer);
  const [length, setLength] = useState<LeafletLength>(LeafletLength.Medium);
  const [result, setResult] = useState('');
  const [generationId, setGenerationId] = useState<string | undefined>(undefined);
  const [isLoading, setIsLoading] = useState(false);
  const [errorBanner, setErrorBanner] = useState<ErrorBanner | null>(null);

  const generate = async () => {
    setIsLoading(true);
    setErrorBanner(null);
    setGenerationId(undefined);
    try {
      const client = getAuthenticatedApiClient();
      const response = await client.leaflet_Generate(new GenerateLeafletRequest({ topic, audience, length }));
      setResult(response.content ?? '');
      setGenerationId(response.id ?? undefined);
    } catch (err: unknown) {
      if (isApiError(err) && err.status === 422) {
        setErrorBanner({
          kind: 'insufficient',
          message:
            err.detail ??
            'Knowledge Base zatím toto téma nepokrývá. Zkuste obecnější formulaci.',
        });
      } else {
        setErrorBanner({
          kind: 'transient',
          message: 'Generování selhalo. Zkuste to prosím znovu.',
        });
      }
    } finally {
      setIsLoading(false);
    }
  };

  return (
    <>
      {errorBanner && (
        <div
          role="alert"
          className={`mb-4 rounded p-3 text-sm ${
            errorBanner.kind === 'insufficient'
              ? 'bg-amber-100 text-amber-900'
              : 'bg-red-100 text-red-900'
          }`}
        >
          {errorBanner.message}
        </div>
      )}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        <div>
          <LeafletForm
            topic={topic}
            audience={audience}
            length={length}
            isLoading={isLoading}
            onTopicChange={setTopic}
            onAudienceChange={setAudience}
            onLengthChange={setLength}
            onSubmit={generate}
          />
        </div>
        <div>
          {isLoading ? (
            <div className="animate-pulse space-y-2">
              <div className="h-4 bg-gray-200 rounded w-3/4" />
              <div className="h-4 bg-gray-200 rounded" />
              <div className="h-4 bg-gray-200 rounded w-5/6" />
            </div>
          ) : (
            <LeafletResult content={result} onRegenerate={generate} generationId={generationId} />
          )}
        </div>
      </div>
    </>
  );
};

export default LeafletGenerateTab;
```

- [ ] **Step 2: Run existing leaflet tab tests**

Run: `cd frontend && npx jest src/features/leaflet-generator`
Expected: PASS (existing test cases didn't assert on the new prop, so they should still pass).

- [ ] **Step 3: Commit**

```bash
git add frontend/src/features/leaflet-generator/LeafletGenerateTab.tsx
git commit -m "feat: pass generationId from generate response to LeafletResult"
```

---

## Task 27: Add i18n translations for new error codes

**Files:**
- Modify: `frontend/src/i18n.ts`

- [ ] **Step 1: Find the Czech Leaflet errors block at line 197–198 and replace**

Replace:

```typescript
        // Leaflet module errors
        LeafletChunkNotFound: "Fragment letáku nebyl nalezen",
```

with:

```typescript
        // Leaflet module errors
        LeafletChunkNotFound: "Fragment letáku nebyl nalezen",
        LeafletFeedbackNotFound: "Záznam generování letáku nebyl nalezen.",
        LeafletFeedbackAlreadySubmitted: "Zpětná vazba již byla odeslána.",
        LeafletGenerationNotFound: "Záznam generování letáku nebyl nalezen.",
```

- [ ] **Step 2: Locate the matching English Leaflet block**

Open the `en` translation block. Find the section that contains `LeafletChunkNotFound`. After it, append:

```typescript
        LeafletFeedbackNotFound: "Leaflet generation log not found.",
        LeafletFeedbackAlreadySubmitted: "Feedback has already been submitted.",
        LeafletGenerationNotFound: "Leaflet generation log not found.",
```

If the English block does not yet contain a `LeafletChunkNotFound` line, add the full set there:

```typescript
        // Leaflet module errors
        LeafletChunkNotFound: "Leaflet chunk not found",
        LeafletFeedbackNotFound: "Leaflet generation log not found.",
        LeafletFeedbackAlreadySubmitted: "Feedback has already been submitted.",
        LeafletGenerationNotFound: "Leaflet generation log not found.",
```

- [ ] **Step 3: Type-check**

Run: `cd frontend && npx tsc --noEmit`
Expected: clean.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/i18n.ts
git commit -m "feat: add i18n entries for leaflet feedback error codes"
```

---

## Task 28: Final verification — backend + frontend full validation

**Files:**
- None modified.

- [ ] **Step 1: Backend full build, format, test**

Run:
```bash
dotnet build backend/Anela.Heblo.sln && \
dotnet format backend/Anela.Heblo.sln --verify-no-changes && \
dotnet test backend/Anela.Heblo.sln --no-build
```
Expected: build clean, format clean, all tests PASS.

- [ ] **Step 2: Frontend build, lint, test**

Run:
```bash
cd frontend && npm run build && npm run lint && npm test -- --watchAll=false
```
Expected: all green.

- [ ] **Step 3: Smoke-test the flow against a local dev environment**

Start the backend and frontend per `docs/development/setup.md`. In the browser:
1. Open the leaflet generator page, submit a topic.
2. Confirm the result panel renders the markdown AND the feedback form below the Copy/Regenerate buttons.
3. Pick precision = 4, style = 5, type a comment, submit. Confirm the form switches to the success state.
4. Reload the page, generate again, attempt to submit feedback — confirm a fresh form is shown (state resets per `generationId`).
5. (Optional, manual) `psql -c 'SELECT "Id", "Topic", "PrecisionScore", "StyleScore", "FeedbackComment" FROM public."LeafletGenerations" ORDER BY "CreatedAt" DESC LIMIT 5;'` — confirm two rows from the smoke test, the first with feedback set.
6. As a user with `leaflet_manager` role, hit `GET /api/leaflet/feedback/list` — confirm 200 with the rows.
7. As a user without `leaflet_manager`, hit the same endpoint — confirm 403.

- [ ] **Step 4: Final commit (no-op or fixups)**

If verification surfaced fixes, commit them with a `fix:` prefix. Otherwise, this task closes the implementation.

---

## Self-Review Checklist (already applied)

- **FR-1** (persist generations) → Tasks 2, 4, 5, 6, 7, 11, 12.
- **FR-2** (one-shot owner-only feedback submission) → Tasks 13, 14.
- **FR-3** (manager-gated feedback list with stats) → Tasks 6, 15, 16, 19.
- **FR-4** (single-generation lookup) → Tasks 17, 18, 19.
- **FR-5** (extract `RagFeedbackForm`) → Tasks 21, 22, 23.
- **FR-6** (leaflet result UI shows form) → Tasks 25, 26.
- **FR-7** (i18n) → Task 27.
- **NFR-1** (perf — server-side paging, indexed sorts, indexed aggregates) → Tasks 4 (indexes), 6 (indexed aggregates).
- **NFR-2** (security — owner check + policy gate + plain-text comment rendering) → Tasks 14 (handler check), 19 (policy on list), 22 (textarea/plain text in form).
- **NFR-3** (reliability — swallow logging errors, single SaveChanges) → Task 11 (try/catch around save), Task 14 (single SaveChanges).
- **NFR-4** (maintainability — KB structural mirror, DTOs as classes) → Tasks 14, 16 — match KB shape.

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-05-05-leaflet-persistence-feedback.md`.
