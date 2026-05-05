Now I have enough grounding. Producing the architecture review.

# Architecture Review: Leaflet Generation Persistence + Feedback (Phase 2)

## Architectural Fit Assessment

The feature lands cleanly inside the existing Clean Architecture / vertical-slice layout. It is a **direct port of the proven KnowledgeBase Q&A persistence pattern** (`KnowledgeBaseQuestionLog` + `QuestionLoggingBehavior` + `SubmitFeedbackHandler` + `GetFeedbackListHandler`) onto the already-shipped Leaflet generation slice (Phase 1). All required infrastructure exists and has been verified in code:

- **MediatR `IPipelineBehavior` registration** in module classes — `KnowledgeBaseModule.cs:58` already does this for Q&A.
- **`BaseResponse` + `ErrorCodes` enum + `HandleResponse` envelope** in `Anela.Heblo.Application.Shared`. `ErrorCodes` is an **enum**, not a class — the spec/brief gets this wrong (string error codes will not compile).
- **`ICurrentUserService`** in `Anela.Heblo.Domain.Features.Users` — already used by the KB flow.
- **`AuthorizationConstants.Policies.LeafletUpload`** exists (`AuthorizationConstants.cs:67`) and is wired in `AuthenticationExtensions.cs:113`.
- **`LeafletController`** already inherits `BaseApiController`, uses `_mediator.Send(...) → HandleResponse(result)`, and applies `[Authorize(Policy = LeafletUpload)]` at the action level for elevated operations — exactly the shape we need.
- **`ApplicationDbContext`** already exposes `KnowledgeBaseQuestionLogs` (`ApplicationDbContext.cs:85`) — adding `LeafletGenerations` slots in next to `LeafletDocuments`/`LeafletChunks`.
- **Migration cadence** is healthy: latest is `20260505080157_AddSummaryToLeafletChunk`. Adding `AddLeafletGenerations` is the next sequential step.

Integration points: (a) `GenerateLeafletHandler` returns `KbSourceCount`/`LeafletSourceCount` and `Id`; (b) the new logging behavior; (c) three new controller actions; (d) a shared frontend feedback component extracted from KB.

## Proposed Architecture

### Component Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│ Frontend (React + TanStack Query)                                   │
│                                                                     │
│  features/leaflet-generator/LeafletResult.tsx                       │
│      │                                                              │
│      │ generationId (from generate response)                        │
│      ▼                                                              │
│  components/feedback/RagFeedbackForm.tsx ◄──── shared ──── KB tab   │
│      │                                                              │
│      │ useSubmitLeafletFeedbackMutation()  (api/hooks/useLeaflet)   │
│      ▼                                                              │
└──────┼──────────────────────────────────────────────────────────────┘
       │ POST /api/leaflet/feedback
       │ GET  /api/leaflet/feedback/list           (LeafletUpload)
       │ GET  /api/leaflet/generations/{id}
       ▼
┌─────────────────────────────────────────────────────────────────────┐
│ API: LeafletController                                              │
│   → IMediator.Send(...) → HandleResponse(BaseResponse)              │
└──────┬──────────────────────────────────────────────────────────────┘
       │
       ▼
┌─────────────────────────────────────────────────────────────────────┐
│ Application/Features/Leaflet                                        │
│                                                                     │
│  Pipeline/                                                          │
│    └── LeafletGenerationLoggingBehavior  (post-success persistence) │
│                                                                     │
│  UseCases/                                                          │
│    ├── GenerateLeaflet/        (Phase 1 – populate Id, counts)      │
│    ├── SubmitLeafletFeedback/  (NEW)                                │
│    ├── GetLeafletFeedbackList/ (NEW)                                │
│    └── GetLeafletGeneration/   (NEW – mirrors GetArticleHandler)    │
└──────┬──────────────────────────────────────────────────────────────┘
       │ ILeafletRepository
       ▼
┌─────────────────────────────────────────────────────────────────────┐
│ Domain/Features/Leaflet                                             │
│   LeafletGeneration (entity), LeafletFeedbackStats (record)         │
│   ILeafletRepository (extended with 4 new methods)                  │
└──────┬──────────────────────────────────────────────────────────────┘
       │
       ▼
┌─────────────────────────────────────────────────────────────────────┐
│ Persistence/Features/Leaflet                                        │
│   LeafletRepository (impl), LeafletGenerationConfiguration          │
│   ApplicationDbContext.LeafletGenerations DbSet                     │
│   Migration: AddLeafletGenerations                                  │
└─────────────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Persistence via MediatR pipeline behavior, not handler-side write
**Options considered:**
1. Inline DB write at the end of `GenerateLeafletHandler.Handle`.
2. Dedicated `IPipelineBehavior<GenerateLeafletRequest, GenerateLeafletResponse>` registered in `LeafletModule`.

**Chosen approach:** Option 2 (matches `QuestionLoggingBehavior`).

**Rationale:** Keeps `GenerateLeafletHandler` focused on the LLM/RAG pipeline; isolates persistence as a cross-cutting concern that can be swapped or disabled without touching domain logic. The KB feature has lived with this split for production usage and the test coverage already validates the pattern.

#### Decision 2: One table, no separate `LeafletGenerationSource` rows
**Options considered:**
1. Store `KbSourceCount`/`LeafletSourceCount` only.
2. Store full per-chunk references in a join table for navigation.

**Chosen approach:** Option 1 (counts only) — explicitly out-of-scope per spec.

**Rationale:** Phase 4+ scope. Adds complexity (FK choreography across two source tables — KB and Leaflet — that have different lifecycles) without serving the Phase 2 use case (quality signal collection). Counts on the single row are sufficient for stats and trend analysis.

#### Decision 3: Owner-only check in the handler, not via `[Authorize]` policy
**Options considered:**
1. Custom authorization handler (`IAuthorizationRequirement`) keyed on `generation.UserId`.
2. Inline `currentUser.Id != generation.UserId → ErrorCodes.Forbidden` in the handler.

**Chosen approach:** Option 2 (matches `SubmitFeedbackHandler.cs:34`).

**Rationale:** The owner check requires loading the entity by id first; folding it into the handler avoids a duplicate lookup and stays consistent with the KB pattern that already ships. The `Forbidden` envelope already carries HTTP 403 via `[HttpStatusCode(...)]` on the enum.

#### Decision 4: Mirror KB stats query (in-memory aggregate) — do not optimize
**Options considered:**
1. Four `CountAsync`/`AverageAsync` round trips (NFR-1's plan).
2. KB's existing pattern: load all feedback rows into memory then compute averages in LINQ-to-Objects (`KnowledgeBaseRepository.cs:299–322`).

**Chosen approach:** **Option 1**, per spec NFR-1, even though it diverges from KB's literal implementation.

**Rationale:** The KB stats query loads every feedback row into memory — that is a latent scaling problem there too. Spec NFR-1 prescribes "four separate aggregates against a single table"; this is a correct optimization, lighter on memory, and still uses the same indexes. The maintainability rule (NFR-4) says "structurally mirror" — structure (separate `GetGenerationStatsAsync` repository call returning a `LeafletFeedbackStats` record) is mirrored; the internal query body legitimately differs. Document this divergence in the repository method.

#### Decision 5: Frontend feedback form extraction — shared component lives under `components/feedback/`
**Options considered:**
1. Duplicate the form in `LeafletResult.tsx`.
2. Extract to `components/feedback/RagFeedbackForm.tsx` with prop-driven onSubmit/state contract.

**Chosen approach:** Option 2, with KB tab refactored to consume it.

**Rationale:** Two consumers already exist (KB Q&A, Leaflet) and the team explicitly anticipates more (Article generation already has its own feedback story queued). Sharing now avoids drift. The contract `{ onSubmit, isSubmitting, alreadySubmitted, isSuccess }` cleanly separates the form UI from the per-feature submit hook.

#### Decision 6: Schema for new table is `public` (not `dbo`)
**Rationale:** The current snapshot (`ApplicationDbContextModelSnapshot.cs:1096`) places `KnowledgeBaseQuestionLogs` in `public`. The original migration created it under `dbo`; a later schema migration moved it. New tables must be created in `public` from the start to match. The brief's statement of `public."LeafletGenerations"` is correct.

## Implementation Guidance

### Directory / Module Structure

**Backend (new files):**
```
backend/src/Anela.Heblo.Domain/Features/Leaflet/
  ├── LeafletGeneration.cs                              [NEW – entity]
  └── LeafletFeedbackStats.cs                           [NEW – record]

backend/src/Anela.Heblo.Persistence/Features/Leaflet/
  └── LeafletGenerationConfiguration.cs                 [NEW]

backend/src/Anela.Heblo.Persistence/Migrations/
  └── 2026MMDDHHMMSS_AddLeafletGenerations.cs           [NEW – via dotnet ef]

backend/src/Anela.Heblo.Application/Features/Leaflet/
  ├── Pipeline/
  │   └── LeafletGenerationLoggingBehavior.cs           [NEW]
  └── UseCases/
      ├── SubmitLeafletFeedback/
      │   ├── SubmitLeafletFeedbackRequest.cs           [NEW]
      │   ├── SubmitLeafletFeedbackResponse.cs          [NEW]
      │   └── SubmitLeafletFeedbackHandler.cs           [NEW]
      ├── GetLeafletFeedbackList/
      │   ├── GetLeafletFeedbackListRequest.cs          [NEW]
      │   ├── GetLeafletFeedbackListResponse.cs         [NEW – includes
      │   │                                              LeafletFeedbackSummary +
      │   │                                              LeafletFeedbackStatsDto]
      │   └── GetLeafletFeedbackListHandler.cs          [NEW]
      └── GetLeafletGeneration/
          ├── GetLeafletGenerationRequest.cs            [NEW]
          ├── GetLeafletGenerationResponse.cs           [NEW]
          └── GetLeafletGenerationHandler.cs            [NEW]
```

**Backend (modified files):**
- `Domain/Features/Leaflet/ILeafletRepository.cs` — add 4 methods.
- `Persistence/Features/Leaflet/LeafletRepository.cs` — implement 4 methods.
- `Persistence/ApplicationDbContext.cs` — add `DbSet<LeafletGeneration>`.
- `Application/Features/Leaflet/LeafletModule.cs` — register the pipeline behavior (use `services.AddScoped<IPipelineBehavior<…>, LeafletGenerationLoggingBehavior>()`).
- `Application/Features/Leaflet/UseCases/GenerateLeaflet/GenerateLeafletResponse.cs` — add `Id` (Guid?), `KbSourceCount`, `LeafletSourceCount`.
- `Application/Features/Leaflet/UseCases/GenerateLeaflet/GenerateLeafletHandler.cs` — set the two count properties on the response.
- `Application/Shared/ErrorCodes.cs` — append `LeafletFeedbackNotFound = 2502` and `LeafletFeedbackAlreadySubmitted = 2503` (next free codes in the 25XX Leaflet block; 2501 is `LeafletChunkNotFound`). Annotate with `[HttpStatusCode(HttpStatusCode.NotFound)]` and `[HttpStatusCode(HttpStatusCode.Conflict)]` respectively.
- `API/Controllers/LeafletController.cs` — add the three actions (POST feedback, GET feedback/list with `[Authorize(Policy = LeafletUpload)]`, GET generations/{id}). Keep validation attributes on the request DTO; the controller-level `[Authorize]` already requires authentication.

**Frontend (new files):**
```
frontend/src/components/feedback/RagFeedbackForm.tsx
frontend/src/components/feedback/__tests__/RagFeedbackForm.test.tsx
```

**Frontend (modified files):**
- `frontend/src/components/knowledge-base/KnowledgeBaseSearchAskTab.tsx` — replace inline `FeedbackForm` (lines 91–163) with `<RagFeedbackForm …>`; the page wires its own `useSubmitFeedbackMutation` and translates result → `{ isSuccess, alreadySubmitted }` to drive the shared component.
- `frontend/src/features/leaflet-generator/LeafletResult.tsx` — accept `generationId?: string`, render `<RagFeedbackForm>` below the buttons when present.
- `frontend/src/api/hooks/useLeaflet.ts` — add `useSubmitLeafletFeedbackMutation` (HTTP 409 → `{ alreadySubmitted: true }`, mirror `useSubmitFeedbackMutation` from `useKnowledgeBase.ts:386–409`); add `useLeafletFeedbackListQuery` (staleTime 30_000) gated by `useLeafletUploadPermission`. Add new types (`SubmitLeafletFeedbackRequest`, `SubmitLeafletFeedbackResult`, `LeafletFeedbackSummary`, `LeafletFeedbackStatsDto`, `LeafletFeedbackListParams`).
- `frontend/src/i18n.ts` — append the two new error code translations to both `cs` and `en` resources (the existing KB entries at line 192–193 are the model — add `LeafletFeedbackNotFound` and `LeafletFeedbackAlreadySubmitted` in the same `errors` block).

### Interfaces and Contracts

**Domain entity (class — has identity and lifecycle):**

```csharp
// Anela.Heblo.Domain/Features/Leaflet/LeafletGeneration.cs
public class LeafletGeneration
{
    public Guid Id { get; set; }
    public string Topic { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;     // AudienceType enum name
    public string Length { get; set; } = string.Empty;       // LeafletLength enum name
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

public sealed record LeafletFeedbackStats(
    int TotalGenerations,
    int TotalWithFeedback,
    double? AvgPrecisionScore,
    double? AvgStyleScore);
```

**Repository (additions only — preserve existing `CancellationToken ct = default` convention):**

```csharp
public interface ILeafletRepository
{
    // … existing 17 methods …

    Task SaveGenerationAsync(LeafletGeneration generation, CancellationToken ct = default);
    Task<LeafletGeneration?> GetGenerationByIdAsync(Guid id, CancellationToken ct = default);
    Task<(IReadOnlyList<LeafletGeneration> Items, int TotalCount)> GetGenerationsPagedAsync(
        bool? hasFeedback, string? userId, string sortBy, bool sortDescending,
        int pageNumber, int pageSize, CancellationToken ct = default);
    Task<LeafletFeedbackStats> GetGenerationStatsAsync(CancellationToken ct = default);
    // SaveChangesAsync(...) already exists at line 23 — reuse for feedback writes.
}
```

**EF Configuration — schema and indexes:**

```csharp
builder.ToTable("LeafletGenerations", "public");
// indexes
builder.HasIndex(g => g.CreatedAt);
builder.HasIndex(g => g.UserId);
builder.HasIndex(g => g.PrecisionScore).HasFilter("\"PrecisionScore\" IS NOT NULL");
```

**Application DTOs (classes per project convention; never records — OpenAPI client-generation gotcha per CLAUDE.md):**

```csharp
public class SubmitLeafletFeedbackRequest : IRequest<SubmitLeafletFeedbackResponse>
{
    public Guid GenerationId { get; set; }
    [Range(1, 5)] public int PrecisionScore { get; set; }
    [Range(1, 5)] public int StyleScore { get; set; }
    [MaxLength(1000)] public string? Comment { get; set; }
}

public class SubmitLeafletFeedbackResponse : BaseResponse
{
    public SubmitLeafletFeedbackResponse() { }
    public SubmitLeafletFeedbackResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
```

**⚠ Spec correction:** The spec uses `string errorCode` in the response constructor — that will not compile. `BaseResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)` requires the **`ErrorCodes` enum**.

**`GenerateLeafletResponse` additions (must be `public set`, not `init`, because the pipeline behavior writes them):**

```csharp
[JsonPropertyName("id")]
public Guid? Id { get; set; }

[JsonPropertyName("kbSourceCount")]
public int KbSourceCount { get; set; }

[JsonPropertyName("leafletSourceCount")]
public int LeafletSourceCount { get; set; }
```

**Controller actions (mirror existing patterns in `LeafletController`):**

```csharp
[HttpPost("feedback")]
public async Task<ActionResult<SubmitLeafletFeedbackResponse>> SubmitFeedback(
    [FromBody] SubmitLeafletFeedbackRequest request, CancellationToken ct)
        => HandleResponse(await _mediator.Send(request, ct));

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
    => HandleResponse(await _mediator.Send(new GetLeafletFeedbackListRequest { … }, ct));

[HttpGet("generations/{id:guid}")]
public async Task<ActionResult<GetLeafletGenerationResponse>> GetGeneration(Guid id, CancellationToken ct)
    => HandleResponse(await _mediator.Send(new GetLeafletGenerationRequest { Id = id }, ct));
```

### Data Flow

**Successful generation + logging:**
```
Client → POST /api/leaflet/generate
  → MediatR pipeline:
      ValidationBehavior (existing)
      LeafletGenerationLoggingBehavior (NEW)
        ├── Stopwatch.Start()
        ├── await next() → GenerateLeafletHandler (RAG + LLM, sets KbSourceCount/LeafletSourceCount)
        ├── Stopwatch.Stop()
        ├── if (response.Success) → try/catch:
        │     ├── new LeafletGeneration { …request fields, response.Content,
        │     │                            counts, sw.ElapsedMilliseconds, currentUser.Id }
        │     ├── _repository.SaveGenerationAsync(gen, ct)
        │     └── response.Id = gen.Id
        │   catch (Exception) → _logger.LogError(ex, …) (swallow)
        └── return response
  ← 200 { success, content, id, kbSourceCount, leafletSourceCount }
```

**Feedback submission (one-shot, owner-only):**
```
Client (owner) → POST /api/leaflet/feedback { generationId, precisionScore, styleScore, comment }
  → ValidationBehavior asserts [Range(1,5)] / [MaxLength(1000)]
  → SubmitLeafletFeedbackHandler:
        gen = repo.GetGenerationByIdAsync(id)
        if gen is null               → BaseResponse(LeafletFeedbackNotFound, {generationId})  [404]
        if gen.UserId != currentUser → BaseResponse(Forbidden, {generationId})                [403]
        if gen.PrecisionScore != null
           or gen.StyleScore != null → BaseResponse(LeafletFeedbackAlreadySubmitted, {…})     [409]
        gen.PrecisionScore  = request.PrecisionScore
        gen.StyleScore      = request.StyleScore
        gen.FeedbackComment = request.Comment
        await repo.SaveChangesAsync(ct)        // single SaveChanges on tracked entity
        return SubmitLeafletFeedbackResponse() // success
  ← HandleResponse maps to 200 / 404 / 403 / 409 envelopes
```

**Manager feedback list:**
```
Manager → GET /api/leaflet/feedback/list?hasFeedback&userId&sortBy&sortDescending&pageNumber&pageSize
  → [Authorize(Policy = LeafletUpload)] enforced before MediatR
  → GetLeafletFeedbackListHandler:
        validates sortBy ∈ {CreatedAt, PrecisionScore, StyleScore} (default CreatedAt)
        validates pageSize ∈ {10, 20, 50} (default 20)
        (logs, total) = repo.GetGenerationsPagedAsync(...)
        stats = repo.GetGenerationStatsAsync()
        return response { Logs = logs.Select(…LeafletFeedbackSummary), TotalCount, PageNumber, PageSize, Stats }
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `LeafletGenerationLoggingBehavior` enrolls `next()` failures into the catch and accidentally swallows generation errors. | High | Behavior must call `next()` **outside** the try/catch and check `response.Success` before persisting. Mirror `QuestionLoggingBehavior` exactly — it does not wrap `next()`. The brief's snippet does this correctly; enforce in code review. |
| Pipeline behavior holds an `ILeafletRepository` in a scoped dependency. If the consumer cancels mid-write, partial `SaveChangesAsync` may leak or the response payload may already be returned with `Id = null`. | Medium | Use `cancellationToken` only for `next()`; pass `CancellationToken.None` to the post-hoc save to ensure the row is written even if the client disconnects after the LLM completes. (KB does pass `cancellationToken`; if you preserve symmetry, document the trade-off.) Recommend `CancellationToken.None` here. |
| Spec uses `string errorCode` in response constructors. Compilation failure. | High | Use `ErrorCodes` enum. Add `LeafletFeedbackNotFound = 2502` and `LeafletFeedbackAlreadySubmitted = 2503` to `ErrorCodes.cs` with `[HttpStatusCode(NotFound)]` and `[HttpStatusCode(Conflict)]` annotations. |
| `[Range]` / `[MaxLength]` validation only fires if a `ValidationBehavior` is wired before the new handler. | Medium | Confirm in `Application/AddApplicationServices` that the global `ValidationBehavior<,>` is registered. (KB relies on the same; verify.) |
| Stats query implementation diverges from KB's "load all then average". | Low | Acceptable per NFR-1; document the divergence with a one-line code comment in `GetGenerationStatsAsync` referencing NFR-1 so a future maintainer doesn't "harmonize" it back to the slower KB pattern. |
| Sharing `RagFeedbackForm` between KB and Leaflet introduces visual/behavioral regression in KB. | Medium | FR-5 acceptance criteria already mandates parity; cover with a snapshot/visual smoke test on the KB tab plus the per-form unit tests. |
| Frontend types and OpenAPI client drift — `Id` is `Guid?` server-side and the auto-generated TS client may treat it as `string \| undefined`. The new `useLeafletResult` flow needs to handle `undefined` cleanly. | Low | `LeafletResult.tsx` already gates feedback rendering on `generationId` truthy check; FR-6 acceptance criteria covers this. |
| `SaveGenerationAsync` calls `SaveChangesAsync` internally — if it later runs in a unit-of-work that batches multiple operations, the inner save will partial-commit. | Low | Acceptable for current scope (logging behavior runs after the handler; no other writes in flight). Match KB's `SaveQuestionLogAsync` pattern that calls `SaveChangesAsync` immediately. |
| Owner-only enforcement at handler level only — no middleware or policy. If a future endpoint forgets the check, leakage. | Medium | Document in the handler with one-line comment. Test must cover the `Forbidden` branch explicitly (already in spec test list). |
| Migration creates table without explicit schema, lands in `dbo`, then snapshot lists `public` → out-of-sync. | Medium | Configuration **must** call `builder.ToTable("LeafletGenerations", "public")` (the brief's snippet omits the schema arg — fix this before generating the migration). |
| `FeedbackComment` is rendered with `ReactMarkdown` if devs follow the existing leaflet result layout, leading to XSS via injected markdown. | Medium | Spec NFR-2 mandates plain-text rendering. `RagFeedbackForm` displays the comment in a textarea/plain `<p>`, never via `ReactMarkdown`. Code review must catch this. |

## Specification Amendments

1. **Error code definition site (Step 9 / FR-7):** `ErrorCodes` is an **enum** in `Anela.Heblo.Application.Shared`, not a string-based class. Add the two codes to the existing 25XX Leaflet block:
   ```csharp
   // Leaflet module errors (25XX)
   [HttpStatusCode(HttpStatusCode.NotFound)]
   LeafletChunkNotFound = 2501,                // existing
   [HttpStatusCode(HttpStatusCode.NotFound)]
   LeafletFeedbackNotFound = 2502,             // NEW
   [HttpStatusCode(HttpStatusCode.Conflict)]
   LeafletFeedbackAlreadySubmitted = 2503,     // NEW
   ```
   Update the `SubmitLeafletFeedbackResponse` constructor signature to `(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)`.

2. **Schema in EF configuration (Step 3):** The brief's snippet uses `builder.ToTable("LeafletGenerations")` without a schema. The current snapshot keeps `KnowledgeBaseQuestionLogs` in `public` (`ApplicationDbContextModelSnapshot.cs:1096`). Change to `builder.ToTable("LeafletGenerations", "public")` to land the new table in the same schema.

3. **CancellationToken signature in repository (Step 2):** Match the existing `ILeafletRepository` style — every method ends with `CancellationToken ct = default`, not `CancellationToken cancellationToken`. The brief uses the latter; align to existing convention.

4. **`GenerateLeafletResponse` field additions (Step 6):** Spec mentions only `Id`. It must also expose `KbSourceCount` and `LeafletSourceCount` — the behavior reads them from the response, and `GenerateLeafletHandler` already has the source counts in `kbHits.Count`/`leafletHits.Count`. Wire those at the bottom of `GenerateLeafletHandler.Handle` before returning.

5. **`GetLeafletGenerationResponse` not-found error code (FR-4):** Spec is ambiguous between `LeafletFeedbackNotFound` and "an equivalent". Recommend a separate `LeafletGenerationNotFound = 2504` (NotFound) — using the feedback-flavored code for a generic GET is misleading. Adds one code; matches Article's `ArticleNotFound` precedent.

6. **`LeafletGenerationLoggingBehavior` cancellation token (Step 7):** Pass `CancellationToken.None` (not the request `cancellationToken`) to `_repository.SaveGenerationAsync(...)` so a client disconnect after `next()` returns does not lose the audit row. The brief's snippet passes the user's token; flip this.

7. **`LeafletModule` — must register the behavior with the right service lifetime.** Use `services.AddScoped<...>` (matches `KnowledgeBaseModule.cs:58`); the brief is consistent, but make sure the `MediatR` package version's `IPipelineBehavior` resolution finds it (the existing KB registration is the proof point).

8. **Frontend hook — `useLeafletFeedbackListQuery` is described in spec but no UI consumes it in Phase 2.** Either (a) drop the hook from Phase 2 (YAGNI), or (b) wire it to a hidden/admin route now. Recommend (a): add the hook only when the consumer (admin dashboard) ships in a later phase.

9. **i18n key alignment:** KB uses `KnowledgeBaseFeedbackLogNotFound` (note the "Log" infix) and `KnowledgeBaseFeedbackAlreadySubmitted`. The new keys (`LeafletFeedbackNotFound`, `LeafletFeedbackAlreadySubmitted`) are simpler and consistent with the new enum names — keep the spec's choice; the KB key name is legacy.

## Prerequisites

1. **Phase 1 merged into `feature/genai_consistency`.** `LeafletController.Generate`, `GenerateLeafletHandler`, `LeafletResult.tsx`, and the leaflet sidebar/route must all be present. Verified in code at HEAD of the worktree.
2. **Branch hygiene:** Cut `feat/leaflet-persistence-feedback` (or similar) **from `feature/genai_consistency`** and target the PR back to `feature/genai_consistency` — not `main`. Hard requirement from `brief.md`.
3. **EF Core tooling installed locally** for `dotnet ef migrations add AddLeafletGenerations --project backend/src/Anela.Heblo.Persistence --startup-project backend/src/Anela.Heblo.API`.
4. **No new infrastructure required** — Postgres, MediatR, MSAL, `ICurrentUserService`, `LeafletUpload` policy, OpenAPI generator are all in place.
5. **No env-var changes** — `LeafletOptions` already binds; no new options keys are introduced.
6. **Manual migration step at deploy time** — per CLAUDE.md, DB migrations are not automated. After merge, run `dotnet ef database update` against the dev DB before testing the new endpoints.