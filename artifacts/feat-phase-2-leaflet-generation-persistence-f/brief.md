# Phase 2 — Leaflet Generation Persistence + Feedback

CRITICAL: use branch "feature/genai_consistency" as an integration branch instead of main. New branch must be based on that and also PR must be targeted back to this branch

## Goal
Every generated leaflet is persisted with its source references. Users can submit a 1–5 precision/style rating and comment. The same owner-only, one-shot feedback pattern as KnowledgeBase applies.

## Dependency
Phase 1 must be complete (sidebar, routes). Otherwise independent from Phase 3/4.

---

## Step 1 — Domain: new entity `LeafletGeneration`

**New file**: `backend/src/Anela.Heblo.Domain/Features/Leaflet/LeafletGeneration.cs`

```csharp
namespace Anela.Heblo.Domain.Features.Leaflet;

public class LeafletGeneration
{
    public Guid Id { get; set; }
    public string Topic { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;   // AudienceType enum name
    public string Length { get; set; } = string.Empty;     // LeafletLength enum name
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

> No separate `LeafletGenerationSource` table — source counts are sufficient for the feedback story. Individual chunk references can be added later if cross-navigation to chunks is required (Phase 4+ scope).

---

## Step 2 — Domain: update repository interface

**File**: `backend/src/Anela.Heblo.Domain/Features/Leaflet/ILeafletRepository.cs`

Add methods:

```csharp
Task SaveGenerationAsync(LeafletGeneration generation, CancellationToken cancellationToken);
Task<LeafletGeneration?> GetGenerationByIdAsync(Guid id, CancellationToken cancellationToken);
Task<(IReadOnlyList<LeafletGeneration> Items, int TotalCount)> GetGenerationsPagedAsync(
    bool? hasFeedback, string? userId, string sortBy, bool descending,
    int page, int pageSize, CancellationToken cancellationToken);
Task<LeafletFeedbackStats> GetGenerationStatsAsync(CancellationToken cancellationToken);
Task SaveChangesAsync(CancellationToken cancellationToken);
```

**New file**: `backend/src/Anela.Heblo.Domain/Features/Leaflet/LeafletFeedbackStats.cs`

```csharp
namespace Anela.Heblo.Domain.Features.Leaflet;

public sealed record LeafletFeedbackStats(
    int TotalGenerations,
    int TotalWithFeedback,
    double? AvgPrecisionScore,
    double? AvgStyleScore);
```

---

## Step 3 — Persistence: EF configuration

**New file**: `backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletGenerationConfiguration.cs`

```csharp
using Anela.Heblo.Domain.Features.Leaflet;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Features.Leaflet;

public class LeafletGenerationConfiguration : IEntityTypeConfiguration<LeafletGeneration>
{
    public void Configure(EntityTypeBuilder<LeafletGeneration> builder)
    {
        builder.ToTable("LeafletGenerations");
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Topic).HasMaxLength(200).IsRequired();
        builder.Property(g => g.Audience).HasMaxLength(50).IsRequired();
        builder.Property(g => g.Length).HasMaxLength(50).IsRequired();
        builder.Property(g => g.FinalMarkdown).IsRequired();
        builder.Property(g => g.UserId).HasMaxLength(200);
        builder.Property(g => g.FeedbackComment).HasColumnType("text");
        builder.HasIndex(g => g.CreatedAt);
        builder.HasIndex(g => g.UserId);
        builder.HasIndex(g => g.PrecisionScore).HasFilter("\"PrecisionScore\" IS NOT NULL");
    }
}
```

**File**: `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs`

Add DbSet after the existing Leaflet DbSets:

```csharp
public DbSet<LeafletGeneration> LeafletGenerations { get; set; } = null!;
```

---

## Step 4 — Persistence: repository implementation

**File**: `backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletRepository.cs`

Implement the four new interface methods:

```csharp
public async Task SaveGenerationAsync(LeafletGeneration generation, CancellationToken cancellationToken)
{
    _context.LeafletGenerations.Add(generation);
    await _context.SaveChangesAsync(cancellationToken);
}

public async Task<LeafletGeneration?> GetGenerationByIdAsync(Guid id, CancellationToken cancellationToken)
    => await _context.LeafletGenerations.FindAsync([id], cancellationToken);

public async Task<(IReadOnlyList<LeafletGeneration> Items, int TotalCount)> GetGenerationsPagedAsync(
    bool? hasFeedback, string? userId, string sortBy, bool descending,
    int page, int pageSize, CancellationToken cancellationToken)
{
    var query = _context.LeafletGenerations.AsQueryable();

    if (hasFeedback == true)
        query = query.Where(g => g.PrecisionScore != null || g.StyleScore != null);
    else if (hasFeedback == false)
        query = query.Where(g => g.PrecisionScore == null && g.StyleScore == null);

    if (!string.IsNullOrWhiteSpace(userId))
        query = query.Where(g => g.UserId == userId);

    query = (sortBy, descending) switch
    {
        ("PrecisionScore", true)  => query.OrderByDescending(g => g.PrecisionScore),
        ("PrecisionScore", false) => query.OrderBy(g => g.PrecisionScore),
        ("StyleScore", true)      => query.OrderByDescending(g => g.StyleScore),
        ("StyleScore", false)     => query.OrderBy(g => g.StyleScore),
        (_, true)                 => query.OrderByDescending(g => g.CreatedAt),
        _                         => query.OrderBy(g => g.CreatedAt),
    };

    var total = await query.CountAsync(cancellationToken);
    var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
    return (items, total);
}

public async Task<LeafletFeedbackStats> GetGenerationStatsAsync(CancellationToken cancellationToken)
{
    var total = await _context.LeafletGenerations.CountAsync(cancellationToken);
    var withFeedback = await _context.LeafletGenerations
        .CountAsync(g => g.PrecisionScore != null || g.StyleScore != null, cancellationToken);
    var avgPrecision = await _context.LeafletGenerations
        .Where(g => g.PrecisionScore != null)
        .AverageAsync(g => (double?)g.PrecisionScore, cancellationToken);
    var avgStyle = await _context.LeafletGenerations
        .Where(g => g.StyleScore != null)
        .AverageAsync(g => (double?)g.StyleScore, cancellationToken);
    return new LeafletFeedbackStats(total, withFeedback, avgPrecision, avgStyle);
}

public Task SaveChangesAsync(CancellationToken cancellationToken)
    => _context.SaveChangesAsync(cancellationToken);
```

---

## Step 5 — Migration

Name: `AddLeafletGenerations` (next sequential after `20260505080157_AddSummaryToLeafletChunk`).

```
dotnet ef migrations add AddLeafletGenerations \
  --project backend/src/Anela.Heblo.Persistence \
  --startup-project backend/src/Anela.Heblo.API
```

Expected migration creates table `public."LeafletGenerations"` with all columns and indexes from Step 3.

---

## Step 6 — Application: `GenerateLeafletResponse` — add `Id`

**File**: `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/GenerateLeafletResponse.cs`

Add:
```csharp
public Guid? Id { get; set; }
```

(Nullable — if the logging behavior fails to write, the generation still succeeds.)

---

## Step 7 — Application: `LeafletGenerationLoggingBehavior`

**New file**: `backend/src/Anela.Heblo.Application/Features/Leaflet/Pipeline/LeafletGenerationLoggingBehavior.cs`

Pattern mirrors `QuestionLoggingBehavior` exactly:

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
                FinalMarkdown = response.Content ?? string.Empty,
                KbSourceCount = response.KbSourceCount,
                LeafletSourceCount = response.LeafletSourceCount,
                DurationMs = sw.ElapsedMilliseconds,
                CreatedAt = DateTimeOffset.UtcNow,
                UserId = currentUser.Id,
            };

            await _repository.SaveGenerationAsync(generation, cancellationToken);
            response.Id = generation.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log leaflet generation. Topic: {Topic}", request.Topic);
        }

        return response;
    }
}
```

Note: `GenerateLeafletResponse` must expose `KbSourceCount` and `LeafletSourceCount`. Check the current response — if those counts are not there, add them in `GenerateLeafletHandler` when building the response (it already has the `kbHits`/`leafletHits` lists).

---

## Step 8 — Register the behavior

**File**: `backend/src/Anela.Heblo.Application/Features/Leaflet/LeafletModule.cs`

Add (after existing service registrations):
```csharp
using Anela.Heblo.Application.Features.Leaflet.Pipeline;
using Anela.Heblo.Application.Features.Leaflet.UseCases.GenerateLeaflet;
// ...

services.AddScoped<
    IPipelineBehavior<GenerateLeafletRequest, GenerateLeafletResponse>,
    LeafletGenerationLoggingBehavior>();
```

---

## Step 9 — Application: `SubmitLeafletFeedback`

**New files** under `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/SubmitLeafletFeedback/`:

`SubmitLeafletFeedbackRequest.cs`:
```csharp
using MediatR;
using Anela.Heblo.Application.Shared;
using System.ComponentModel.DataAnnotations;

namespace Anela.Heblo.Application.Features.Leaflet.UseCases.SubmitLeafletFeedback;

public class SubmitLeafletFeedbackRequest : IRequest<SubmitLeafletFeedbackResponse>
{
    public Guid GenerationId { get; set; }
    [Range(1, 5)] public int PrecisionScore { get; set; }
    [Range(1, 5)] public int StyleScore { get; set; }
    [MaxLength(1000)] public string? Comment { get; set; }
}
```

`SubmitLeafletFeedbackResponse.cs`:
```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Leaflet.UseCases.SubmitLeafletFeedback;

public class SubmitLeafletFeedbackResponse : BaseResponse
{
    public SubmitLeafletFeedbackResponse() { }
    public SubmitLeafletFeedbackResponse(string errorCode, Dictionary<string, string>? context = null)
        : base(errorCode, context) { }
}
```

`SubmitLeafletFeedbackHandler.cs` — mirror `SubmitFeedbackHandler` from KB exactly:
```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Leaflet;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.Leaflet.UseCases.SubmitLeafletFeedback;

public class SubmitLeafletFeedbackHandler : IRequestHandler<SubmitLeafletFeedbackRequest, SubmitLeafletFeedbackResponse>
{
    private readonly ILeafletRepository _repository;
    private readonly ICurrentUserService _currentUserService;

    public SubmitLeafletFeedbackHandler(ILeafletRepository repository, ICurrentUserService currentUserService)
    {
        _repository = repository;
        _currentUserService = currentUserService;
    }

    public async Task<SubmitLeafletFeedbackResponse> Handle(
        SubmitLeafletFeedbackRequest request, CancellationToken cancellationToken)
    {
        var generation = await _repository.GetGenerationByIdAsync(request.GenerationId, cancellationToken);
        if (generation is null)
            return new SubmitLeafletFeedbackResponse(ErrorCodes.LeafletFeedbackNotFound,
                new() { { "generationId", request.GenerationId.ToString() } });

        var currentUser = _currentUserService.GetCurrentUser();
        if (generation.UserId != currentUser.Id)
            return new SubmitLeafletFeedbackResponse(ErrorCodes.Forbidden,
                new() { { "generationId", request.GenerationId.ToString() } });

        if (generation.PrecisionScore is not null || generation.StyleScore is not null)
            return new SubmitLeafletFeedbackResponse(ErrorCodes.LeafletFeedbackAlreadySubmitted,
                new() { { "generationId", request.GenerationId.ToString() } });

        generation.PrecisionScore = request.PrecisionScore;
        generation.StyleScore = request.StyleScore;
        generation.FeedbackComment = request.Comment;

        await _repository.SaveChangesAsync(cancellationToken);
        return new SubmitLeafletFeedbackResponse();
    }
}
```

Add error codes to `ErrorCodes` enum/class:
- `LeafletFeedbackNotFound`
- `LeafletFeedbackAlreadySubmitted`

---

## Step 10 — Application: `GetLeafletFeedbackList`

**New files** under `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletFeedbackList/`:

Follow the exact shape of KB's `GetFeedbackListHandler`, `GetFeedbackListRequest`, `GetFeedbackListResponse`, `FeedbackLogSummary`, `FeedbackStatsDto` — rename with `Leaflet` prefix.

`GetLeafletFeedbackListRequest.cs`:
```csharp
public class GetLeafletFeedbackListRequest : IRequest<GetLeafletFeedbackListResponse>
{
    public bool? HasFeedback { get; set; }
    public string? UserId { get; set; }
    public string SortBy { get; set; } = "CreatedAt";
    public bool SortDescending { get; set; } = true;
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
```

`GetLeafletFeedbackListResponse.cs`:
```csharp
public class GetLeafletFeedbackListResponse : BaseResponse
{
    public List<LeafletFeedbackSummary> Logs { get; set; } = [];
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
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
}

public class LeafletFeedbackStatsDto
{
    public int TotalGenerations { get; set; }
    public int TotalWithFeedback { get; set; }
    public double? AvgPrecisionScore { get; set; }
    public double? AvgStyleScore { get; set; }
}
```

Handler follows `GetFeedbackListHandler` exactly, validated sort columns: `["CreatedAt", "PrecisionScore", "StyleScore"]`, allowed page sizes: `[10, 20, 50]`.

---

## Step 11 — API controller additions

**File**: `backend/src/Anela.Heblo.API/Controllers/LeafletController.cs`

Add three new actions:

```csharp
// using ... (add required using statements for new use cases)

[HttpPost("feedback")]
[ProducesResponseType(typeof(SubmitLeafletFeedbackResponse), 200)]
[ProducesResponseType(typeof(ProblemDetails), 409)]
public async Task<ActionResult<SubmitLeafletFeedbackResponse>> SubmitFeedback(
    [FromBody] SubmitLeafletFeedbackRequest request, CancellationToken ct)
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
public async Task<ActionResult<GetLeafletGenerationResponse>> GetGeneration(Guid id, CancellationToken ct)
{
    // Simple handler: load LeafletGeneration by id, return 404 if not found
    var result = await _mediator.Send(new GetLeafletGenerationRequest { Id = id }, ct);
    return HandleResponse(result);
}
```

> `GetLeafletGenerationRequest/Handler/Response` is a trivial loader — create the three files following the same pattern as `GetArticleHandler`.

---

## Step 12 — Frontend: extract `RagFeedbackForm`

**New file**: `frontend/src/components/feedback/RagFeedbackForm.tsx`

Extract the feedback form from `KnowledgeBaseSearchAskTab.tsx` (lines ~93-163). The component currently knows about KB-specific hook shapes. Generalize via props:

```tsx
interface RagFeedbackFormProps {
  onSubmit: (data: { precisionScore: number; styleScore: number; comment: string }) => void;
  isSubmitting: boolean;
  alreadySubmitted: boolean;
  isSuccess: boolean;
}

export default function RagFeedbackForm({ onSubmit, isSubmitting, alreadySubmitted, isSuccess }: RagFeedbackFormProps) {
  // ... rating UI, textarea, submit button
}
```

Update `KnowledgeBaseSearchAskTab.tsx` to import and use `RagFeedbackForm` instead of the inline form.

---

## Step 13 — Frontend: `LeafletResult.tsx` — add feedback form

**File**: `frontend/src/features/leaflet-generator/LeafletResult.tsx`

Update `LeafletResultProps` to accept an optional `generationId`:
```tsx
interface LeafletResultProps {
  content: string;
  generationId?: string;
  onRegenerate: () => void;
}
```

Add feedback form below the Copy/Regenerate buttons using `RagFeedbackForm`. Wire to a new `useSubmitLeafletFeedbackMutation` hook.

---

## Step 14 — Frontend: `useLeaflet.ts` — add feedback hooks

**File**: `frontend/src/api/hooks/useLeaflet.ts`

Add:
```ts
export function useSubmitLeafletFeedbackMutation() {
  return useMutation({
    mutationFn: (data: { generationId: string; precisionScore: number; styleScore: number; comment?: string }) =>
      client.leaflet_SubmitFeedback(data),
    // handle HTTP 409 as alreadySubmitted (same pattern as KB)
  });
}

export function useLeafletFeedbackListQuery(params: LeafletFeedbackListParams) {
  return useQuery({
    queryKey: leafletKeys.feedbackList(params),
    queryFn: () => client.leaflet_GetFeedbackList(params),
    staleTime: 30_000,
  });
}
```

---

## Step 15 — i18n

**File**: `frontend/src/i18n.ts`

Add error code translations:
```ts
LeafletFeedbackNotFound: {
  cs: 'Záznam generování letáku nebyl nalezen.',
  en: 'Leaflet generation log not found.',
},
LeafletFeedbackAlreadySubmitted: {
  cs: 'Zpětná vazba již byla odeslána.',
  en: 'Feedback has already been submitted.',
},
```

---

## Tests to write

### Backend
- `SubmitLeafletFeedbackHandlerTests` — not found / forbidden / already submitted / success
- `GetLeafletFeedbackListHandlerTests` — paging, filters, stats
- `LeafletGenerationLoggingBehaviorTests` — verify generation saved, `response.Id` populated, errors swallowed
- `LeafletRepositoryIntegrationTests` — `SaveGenerationAsync`, `GetGenerationByIdAsync`, `GetGenerationsPagedAsync`, `GetGenerationStatsAsync`

### Frontend
- `useLeaflet.test.ts` — `useSubmitLeafletFeedbackMutation` converts 409 → `alreadySubmitted`
- `RagFeedbackForm.test.tsx` — renders ratings, submits, shows already-submitted state

---

## Verification

1. `dotnet build` + `dotnet format`.
2. `npm run build` + `npm run lint`.
3. Run new backend tests: all green.
4. Migration applies cleanly: `dotnet ef database update` on local dev DB.
5. Generate a leaflet → inspect DB: row exists in `LeafletGenerations` with correct topic, counts, markdown.
6. `response.id` returned by API → feedback form shown.
7. Submit feedback → `PrecisionScore`/`StyleScore` updated in DB.
8. Submit again → HTTP 409 → UI shows "Zpětná vazba již byla odeslána."
9. `GET /api/leaflet/feedback/list` (with manager role) → returns paged data + stats.