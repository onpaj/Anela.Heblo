# Architecture Review: Phase 2 — Leaflet Generation Persistence + Feedback

## Architectural Fit Assessment

The spec is a near-verbatim transplant of an already-validated pattern: `KnowledgeBaseQuestionLog` + `QuestionLoggingBehavior` + `SubmitFeedback*` + `GetFeedbackList*` + the inline KB feedback form. Every architectural choice in the spec maps to a confirmed precedent in this codebase:

- **MediatR pipeline behavior for post-execution logging** — established by `KnowledgeBaseModule.cs:58` registering `QuestionLoggingBehavior` scoped per request type.
- **Owner-only one-shot feedback workflow** — exact shape exists in `SubmitFeedbackHandler` (KB), including the `PrecisionScore is not null || StyleScore is not null` already-submitted check.
- **Manager-only list with stats** — `GetFeedbackListHandler` already provides the exact pattern (sort whitelist, pagesize whitelist, repository-level paging tuple, stats DTO).
- **Frontend mutation that maps HTTP 409 → `alreadySubmitted`** — `useSubmitFeedbackMutation` in `useKnowledgeBase.ts:386–409` does this today.
- **Vertical Slice + Clean Architecture** — placement under `Features/Leaflet/UseCases/<UseCase>/...` is already the project norm.

Integration points: `ApplicationDbContext` (new DbSet), `ILeafletRepository` (four new methods), `ErrorCodes` enum (two new entries + HttpStatusCode attributes), `LeafletModule` (one pipeline behavior registration), `LeafletController` (three new actions), OpenAPI TypeScript client (auto-regenerated on build), frontend `useLeaflet.ts`, new `RagFeedbackForm` component reused by KB.

The feature requires no new infrastructure, no new packages, and no architectural deviation. **Low architectural risk; the implementation effort is largely mechanical mirroring of the KB module.**

## Proposed Architecture

### Component Overview

```
┌────────────────────────── HTTP layer ──────────────────────────────┐
│  LeafletController                                                 │
│   ├─ POST   /api/leaflet/feedback                  (auth)          │
│   ├─ GET    /api/leaflet/feedback/list             (LeafletUpload) │
│   ├─ GET    /api/leaflet/generations/{id}          (auth)          │
│   └─ POST   /api/leaflet/generate (existing — unchanged path)      │
└────────────────────────────────────────────────────────────────────┘
                                │
                                ▼
┌────────────────────────── MediatR pipeline ────────────────────────┐
│  GenerateLeafletRequest                                            │
│    → ValidationBehavior (existing, outer)                          │
│    → LeafletGenerationLoggingBehavior  ★ NEW (post-handler)        │
│        (saves LeafletGeneration + sets response.Id)                │
│    → GenerateLeafletHandler (now populates SourceCounts)           │
│                                                                    │
│  SubmitLeafletFeedbackRequest      → SubmitLeafletFeedbackHandler  │
│  GetLeafletFeedbackListRequest     → GetLeafletFeedbackListHandler │
│  GetLeafletGenerationRequest       → GetLeafletGenerationHandler   │
└────────────────────────────────────────────────────────────────────┘
                                │
                                ▼
┌──────────────── Domain (Anela.Heblo.Domain) ───────────────────────┐
│  LeafletGeneration  (POCO entity)                                  │
│  LeafletFeedbackStats  (record — domain projection, not on wire)   │
│  ILeafletRepository  (extended with 4 new methods)                 │
└────────────────────────────────────────────────────────────────────┘
                                │
                                ▼
┌──────────── Persistence (Anela.Heblo.Persistence) ─────────────────┐
│  LeafletRepository (EF Core implementation)                        │
│  LeafletGenerationConfiguration (table mapping + indexes)          │
│  Migration: AddLeafletGenerations                                  │
└────────────────────────────────────────────────────────────────────┘

Frontend
  RagFeedbackForm.tsx (extracted, reusable) ─── used by ───┐
                                                            ├─ KnowledgeBaseSearchAskTab
                                                            └─ LeafletResult (+ generationId prop)
  useSubmitLeafletFeedbackMutation, useLeafletFeedbackListQuery (useLeaflet.ts)
```

### Key Design Decisions

#### Decision 1: Synchronous persistence inside the pipeline behavior
**Options considered:**
- (a) Save synchronously inside `LeafletGenerationLoggingBehavior` after `await next()`, swallow exceptions.
- (b) Fire-and-forget (background `Task.Run`) for zero added latency.
- (c) Outbox / queue.

**Chosen approach:** (a), matching `QuestionLoggingBehavior`.

**Rationale:** Keeps the `Id` available on the synchronous response so the frontend can immediately bind the feedback form. Fire-and-forget loses the captured `DurationMs` precision and complicates the response-Id roundtrip. The 50 ms NFR is comfortably met by a single insert with no related entities. Outbox is overkill for a non-critical analytics row.

#### Decision 2: Pipeline behavior registration order
**Options considered:**
- (a) Register `LeafletGenerationLoggingBehavior` after `ValidationBehavior` so validation runs outermost.
- (b) Register before, so logging wraps validation failures.

**Chosen approach:** (a), mirroring the KB module.

**Rationale:** A `GenerateLeafletRequest` that fails validation never produced content and should not write a generation row. MediatR executes behaviors in registration order outermost-first; registering the logging behavior **after** the global `ValidationBehavior` ensures validation short-circuits before the logging behavior runs. The inner handler's response `Success == false` branch is the second guard.

#### Decision 3: `ErrorCodes` extension uses `[HttpStatusCode]` attributes (not free-form strings)
**Spec says:** `new SubmitLeafletFeedbackResponse(ErrorCodes.LeafletFeedbackNotFound, …)` but the constructor signature in the spec is `(string errorCode, …)`.

**Reality (verified at `Anela.Heblo.Application/Shared/BaseResponse.cs`):** `BaseResponse(ErrorCodes errorCode, Dictionary<string,string>? parameters = null)` — the parameter is the **`ErrorCodes` enum**, not a string.

**Chosen approach:** Add two new enum members in the 25XX (Leaflet) range with HTTP status attributes:
```csharp
[HttpStatusCode(HttpStatusCode.NotFound)]   LeafletFeedbackNotFound = 2502,
[HttpStatusCode(HttpStatusCode.Conflict)]   LeafletFeedbackAlreadySubmitted = 2503,
```
`Forbidden = 0014` already exists and maps via `BaseApiController.HandleResponse` to `Forbid()` (HTTP 403 without body — same behavior the KB feedback handler already relies on).

**Rationale:** The HTTP-status mapping is reflection-driven from `[HttpStatusCode]`. The spec's "Maps to HTTP 409 for `LeafletFeedbackAlreadySubmitted`" only works if the attribute is present; without it, `BaseApiController.HandleResponse` falls through to `BadRequest`.

#### Decision 4: New endpoints use `HandleResponse`; existing `POST /generate` keeps its custom try/catch
**Options considered:** (a) Migrate `Generate` to `HandleResponse` for consistency, (b) leave it alone.

**Chosen approach:** (b) — surgical changes only.

**Rationale:** The existing `Generate` action catches `EmptyRetrievalException` and maps to 422; converting it to the `BaseResponse`/`HandleResponse` envelope is a behavioral change outside the spec's scope. The three new actions use `HandleResponse` because their responses inherit `BaseResponse` and that's the convention used by KB's feedback endpoints (`KnowledgeBaseController.cs:94–124`).

#### Decision 5: `LeafletFeedbackStats` as `record` (domain) but `LeafletFeedbackStatsDto` as `class` (wire)
**Project rule:** "DTOs are classes, never C# records" — OpenAPI generator mishandles record positional parameter order.

**Chosen approach:** Keep the spec as written: domain projection is a record (never serialized), wire DTO is a class.

**Rationale:** Mirrors the KB split exactly. The record never reaches `ApplicationDbContext` mapping or the HTTP boundary; it is converted in the handler.

#### Decision 6: `GET /api/leaflet/generations/{id}` authorization
**Spec says:** "authentication only … unless existing convention dictates otherwise."

**Issue:** A returned row may include `FeedbackComment` written by a different user. Permitting any authenticated user to load any generation by GUID exposes other users' content (low risk: GUIDs are not enumerable, but the contract is loose).

**Chosen approach:** Require **owner OR `LeafletUpload` policy**. Implement in handler: load entity, then return `Forbidden` if `generation.UserId != currentUser.Id` and the user is not in the `LeafletUpload` policy.

**Rationale:** Matches the principle that managers (already gated to `LeafletUpload` for the list endpoint) can drill into any row, while regular users only see their own. KB does not have a single-row endpoint, so there is no precedent to copy verbatim — pick the safer default.

#### Decision 7: Length/Audience persisted as enum **name**, not enum value
**Spec choice:** `Audience.ToString()`, `Length.ToString()`.

**Trade-off accepted:** Renaming the enum members in code silently invalidates historical rows for filtering. Storing strings is debuggable and matches spec intent (this is an analytics log, not a referential constraint). Fine for Phase 2; flag as a known constraint.

## Implementation Guidance

### Directory / Module Structure

New files (paths verified against existing conventions):

```
backend/src/Anela.Heblo.Domain/Features/Leaflet/
  ├─ LeafletGeneration.cs                    [POCO entity]
  └─ LeafletFeedbackStats.cs                 [record — domain projection]

backend/src/Anela.Heblo.Persistence/Features/Leaflet/
  └─ LeafletGenerationConfiguration.cs       [EF mapping]

backend/src/Anela.Heblo.Persistence/Migrations/
  └─ <timestamp>_AddLeafletGenerations.cs    [next sequential after 20260505080157]

backend/src/Anela.Heblo.Application/Features/Leaflet/
  ├─ Pipeline/
  │   └─ LeafletGenerationLoggingBehavior.cs
  └─ UseCases/
      ├─ SubmitLeafletFeedback/
      │   ├─ SubmitLeafletFeedbackRequest.cs
      │   ├─ SubmitLeafletFeedbackResponse.cs
      │   └─ SubmitLeafletFeedbackHandler.cs
      ├─ GetLeafletFeedbackList/
      │   ├─ GetLeafletFeedbackListRequest.cs
      │   ├─ GetLeafletFeedbackListResponse.cs   [+ LeafletFeedbackSummary, LeafletFeedbackStatsDto]
      │   └─ GetLeafletFeedbackListHandler.cs
      └─ GetLeafletGeneration/
          ├─ GetLeafletGenerationRequest.cs
          ├─ GetLeafletGenerationResponse.cs
          └─ GetLeafletGenerationHandler.cs

frontend/src/components/feedback/
  ├─ RagFeedbackForm.tsx                     [extracted from KB]
  └─ RagFeedbackForm.test.tsx
```

Modified files:
- `backend/src/Anela.Heblo.Domain/Features/Leaflet/ILeafletRepository.cs` (+4 methods)
- `backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletRepository.cs` (+4 implementations)
- `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs` (+1 DbSet, after existing Leaflet DbSets at lines 86–87)
- `backend/src/Anela.Heblo.Application/Features/Leaflet/LeafletModule.cs` (+1 pipeline registration)
- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/GenerateLeafletResponse.cs` (+`Id`, +`KbSourceCount`, +`LeafletSourceCount`)
- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/GenerateLeafletHandler.cs` (populate the two source counts in the success branch)
- `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` (+2 entries with HttpStatusCode attributes)
- `backend/src/Anela.Heblo.API/Controllers/LeafletController.cs` (+3 actions; verify it inherits `BaseApiController`)
- `frontend/src/api/hooks/useLeaflet.ts` (+2 hooks, +query keys)
- `frontend/src/features/leaflet-generator/LeafletResult.tsx` (+`generationId?` prop, +form integration)
- `frontend/src/components/knowledge-base/KnowledgeBaseSearchAskTab.tsx` (replace inline form with `<RagFeedbackForm>`)
- `frontend/src/i18n.ts` (+2 leaflet error keys; consider also closing the existing KB gap noted below)

### Interfaces and Contracts

**Repository extension** (verbatim from spec, on `ILeafletRepository`):
```csharp
Task SaveGenerationAsync(LeafletGeneration generation, CancellationToken ct);
Task<LeafletGeneration?> GetGenerationByIdAsync(Guid id, CancellationToken ct);
Task<(IReadOnlyList<LeafletGeneration> Items, int TotalCount)> GetGenerationsPagedAsync(
    bool? hasFeedback, string? userId, string sortBy, bool descending,
    int page, int pageSize, CancellationToken ct);
Task<LeafletFeedbackStats> GetGenerationStatsAsync(CancellationToken ct);
Task SaveChangesAsync(CancellationToken ct);
```

**Pipeline behavior signature** (no new abstractions — uses existing MediatR primitives):
```csharp
public class LeafletGenerationLoggingBehavior
    : IPipelineBehavior<GenerateLeafletRequest, GenerateLeafletResponse>
```

**Handler signatures** mirror KB exactly; no new infrastructure.

**Response constructor pattern** — must use `ErrorCodes` enum, not string:
```csharp
public SubmitLeafletFeedbackResponse(ErrorCodes errorCode, Dictionary<string, string>? details = null)
    : base(errorCode, details) { }
```

**OpenAPI client expectations** — once the controller actions ship, the auto-generated TypeScript client surfaces:
- `client.leaflet_SubmitFeedback(body, …)`
- `client.leaflet_GetFeedbackList({ hasFeedback, userId, sortBy, sortDescending, pageNumber, pageSize })`
- `client.leaflet_GetGeneration(id)`

Frontend hooks must construct absolute URLs as `${apiClient.baseUrl}${relativeUrl}` (per project rule) when bypassing the typed client (the KB feedback hook already does this via `(apiClient as any).http.fetch`).

### Data Flow

**Generate + persist (happy path):**
```
Client POST /api/leaflet/generate
  → ValidationBehavior (passes)
  → LeafletGenerationLoggingBehavior.Handle:
      var sw = Stopwatch.StartNew();
      response = await next();              // GenerateLeafletHandler runs
      sw.Stop();
      if (!response.Success) return response;
      try {
        currentUser = _currentUserService.GetCurrentUser();
        generation = new LeafletGeneration { ... };
        await _repository.SaveGenerationAsync(generation, ct);
        response.Id = generation.Id;
      } catch (Exception ex) { _logger.LogError(...); }
      return response;
  → Controller returns Ok(response) with Id, Content, KbSourceCount, LeafletSourceCount
```

**Submit feedback:**
```
Client POST /api/leaflet/feedback { generationId, precisionScore, styleScore, comment? }
  → ValidationBehavior (Range, MaxLength)
  → SubmitLeafletFeedbackHandler:
      generation = repo.GetGenerationByIdAsync(id);
      if null → LeafletFeedbackNotFound (404)
      if generation.UserId != currentUser.Id → Forbidden (403)
      if PrecisionScore != null || StyleScore != null → LeafletFeedbackAlreadySubmitted (409)
      mutate fields; repo.SaveChangesAsync();
      return Success
  → BaseApiController.HandleResponse → 200 / 404 / 403 / 409 envelope
```

**Manager list:**
```
Client GET /api/leaflet/feedback/list?hasFeedback=true&pageSize=20
  → [Authorize(Policy=LeafletUpload)] gate
  → GetLeafletFeedbackListHandler:
      validate sortBy ∈ {CreatedAt,PrecisionScore,StyleScore} else CreatedAt
      validate pageSize ∈ {10,20,50} else 20
      (items,total) = repo.GetGenerationsPagedAsync(...)
      stats = repo.GetGenerationStatsAsync()
      map → LeafletFeedbackSummary list + LeafletFeedbackStatsDto
  → 200 with envelope
```

**Frontend feedback flow:**
```
LeafletResult receives generationId from generate response
  → user submits scores+comment
  → useSubmitLeafletFeedbackMutation:
      fetch POST /api/leaflet/feedback
      if response.status === 409 → return { alreadySubmitted: true }
      else if !ok → throw
  → onSuccess: form switches to alreadySubmitted/success state
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `SubmitLeafletFeedbackResponse(string, …)` constructor as written in spec compiles only if `ErrorCodes` is reinterpreted; will fail compilation against existing `BaseResponse(ErrorCodes, …)`. | **HIGH** | Use the actual `BaseResponse` signature (enum, not string). See Spec Amendments below. |
| Missing `[HttpStatusCode]` attributes on new error codes → spec's "maps to 409" silently breaks; falls through to 400. | **HIGH** | Add `[HttpStatusCode(HttpStatusCode.NotFound)]` and `[HttpStatusCode(HttpStatusCode.Conflict)]` to the two new enum members. |
| `GenerateLeafletResponse` currently lacks `Id`, `KbSourceCount`, `LeafletSourceCount`; pipeline behavior cannot set fields that don't exist. | **HIGH** | Step 6 of the brief already covers `Id`. Add the two count fields as part of the same step. `GenerateLeafletHandler` already has `kbHits`/`leafletHits` lists (verified) — populate `response.KbSourceCount = kbHits.Count`, `response.LeafletSourceCount = leafletHits.Count` before returning. |
| Pipeline behavior registered before `ValidationBehavior` would write a row even on validation failure (and add latency to invalid requests). | MEDIUM | Register `LeafletGenerationLoggingBehavior` **after** the global `ValidationBehavior` registration in `LeafletModule.cs`, mirroring `KnowledgeBaseModule.cs:58`. Verify by inspecting the module file before adding the registration. |
| OpenAPI TS client must regenerate; if PR builds skip regen, frontend types drift. | MEDIUM | Project rule: client is auto-generated on build. Run `dotnet build` of the API project before `npm run build` (project convention). Verify the new methods appear in the generated client before wiring frontend hooks. |
| `LeafletController.Generate` does not use `HandleResponse`; mixing patterns may confuse reviewers. | LOW | Out of scope. Add a brief code comment on the new actions if needed; do **not** refactor `Generate`. |
| `LeafletGeneration` rows accumulate without retention; `FinalMarkdown` is a `text` column — table grows fast. | LOW | Out of scope per the spec's "Out of Scope" section; document as known concern for Phase 3+. |
| `GET /api/leaflet/generations/{id}` exposes other users' rows if only authentication is required. | MEDIUM | Adopt **owner-or-policy** authorization in the handler (Decision 6). Reject with `Forbidden` otherwise. |
| Frontend `i18n.ts` has incomplete coverage for existing `KnowledgeBaseFeedbackLogNotFound`. | LOW | Out of scope per FR-7 (only leaflet codes), but worth noting in PR description; could be closed opportunistically by adding the missing KB key in the same i18n edit. Confirm with developer before doing so. |
| Migration filtered index uses PostgreSQL quoted identifier syntax `"PrecisionScore" IS NOT NULL`. | LOW | Verified syntax matches existing migration conventions in this project (Postgres provider). Test by running migration locally before pushing. |
| `LeafletController` may not currently inherit `BaseApiController`; if it doesn't, `HandleResponse` is unavailable. | MEDIUM | Verify base class before writing the new actions. If it inherits a different base, change to `BaseApiController` (matches `KnowledgeBaseController`). |

## Specification Amendments

1. **Replace string constructor signature** in `SubmitLeafletFeedbackResponse` (and `GetLeafletFeedbackListResponse`, `GetLeafletGenerationResponse`):
   ```csharp
   // SPEC SHOWS (incorrect):
   public SubmitLeafletFeedbackResponse(string errorCode, Dictionary<string, string>? context = null)
       : base(errorCode, context) { }

   // CORRECT (matches BaseResponse and KB pattern):
   public SubmitLeafletFeedbackResponse(ErrorCodes errorCode, Dictionary<string, string>? details = null)
       : base(errorCode, details) { }
   ```

2. **Define new `ErrorCodes` members with HTTP status attributes** (Step 9 of the brief):
   ```csharp
   [HttpStatusCode(HttpStatusCode.NotFound)]
   LeafletFeedbackNotFound = 2502,

   [HttpStatusCode(HttpStatusCode.Conflict)]
   LeafletFeedbackAlreadySubmitted = 2503,
   ```
   The 25XX range already starts with `LeafletChunkNotFound = 2501`; pick the next two values.

3. **Step 6 expanded:** `GenerateLeafletResponse` adds three fields, not one:
   ```csharp
   public Guid? Id { get; set; }
   public int KbSourceCount { get; set; }
   public int LeafletSourceCount { get; set; }
   ```
   And `GenerateLeafletHandler` must set the counts on the success path before returning.

4. **`GET /api/leaflet/generations/{id}` authorization** (Decision 6):
   - Add an explicit owner-or-policy check inside `GetLeafletGenerationHandler`:
     ```
     if (generation.UserId != currentUser.Id && !User.IsInPolicy(LeafletUpload))
         return new GetLeafletGenerationResponse(ErrorCodes.Forbidden, ...);
     ```
   - Note: `User.IsInPolicy` is not a built-in helper; pass policy evaluation through `IAuthorizationService` or perform the policy check at the controller layer with `[Authorize(Policy = LeafletUpload)]` as a fallback. **Recommended:** keep the controller annotation off and inject `IAuthorizationService` into the handler — but the simpler answer is: gate the action with an additional second `[Authorize]` policy variant, or do the owner check in handler and let `LeafletUpload`-policy users use the list endpoint. **Pick the simpler one:** in handler, only allow the owner (return Forbidden otherwise); managers retrieve full rows via the list endpoint. This avoids cross-cutting policy evaluation.

5. **Pipeline behavior registration order** — explicitly note in Step 8 that the registration must come **after** any global `IPipelineBehavior<,>` registrations (e.g. `ValidationBehavior`). Match the placement used in `KnowledgeBaseModule.cs:58`.

6. **Verify `LeafletController` inherits `BaseApiController`** — add a brief verification step before Step 11. If not, change the base class (separate, surgical edit).

7. **Frontend i18n** — the spec's wording "error code translations" should explicitly read: add entries under the existing `errors.<ErrorCode>` namespace in `frontend/src/i18n.ts` (matching the pattern at lines ~71–250). The existing `KnowledgeBaseFeedbackAlreadySubmitted` entry can be used as the literal template.

8. **Test additions** — explicitly include a test asserting the `[HttpStatusCode]` attribute mapping produces 409 for `LeafletFeedbackAlreadySubmitted`. This is a regression risk if the attribute is forgotten; one targeted controller-level test (or assertion via reflection) catches it.

## Prerequisites

1. **Branch base:** Create the new feature branch from `origin/feature/genai_consistency` (CRITICAL per the brief). PR target = `feature/genai_consistency`, NOT `main`.
2. **Phase 1 merged:** sidebar / routes / generate handler must already exist on `feature/genai_consistency` (verified — `LeafletController.Generate`, `GenerateLeafletHandler`, `GenerateLeafletRequest/Response` all present in the worktree).
3. **Database migration application:** per project rule "migrations remain manual." Plan: developer runs `dotnet ef database update` against local dev DB after Step 5; staging migration applied manually before PR merge.
4. **OpenAPI client regeneration:** `dotnet build` of the API project must succeed before `npm run build` so the TypeScript client surfaces the new methods. Frontend work in Steps 13–14 depends on this.
5. **Verification of `BaseApiController` base class** for `LeafletController` (see Spec Amendment #6).
6. **No new packages, no infrastructure changes, no env vars.**
7. **Authorization policies** — `AuthorizationConstants.Policies.LeafletUpload` is already defined and already used by other LeafletController actions; no new policy needed.
8. **Validation:** before declaring complete, run `dotnet build`, `dotnet format`, `npm run build`, `npm run lint`, and the new test suites; verify the migration applies cleanly on a local DB.