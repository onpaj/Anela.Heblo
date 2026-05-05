Exploration complete. Now writing the architecture review.

# Architecture Review: Leaflet Generation Persistence + Feedback (Phase 2)

## Architectural Fit Assessment
This feature aligns cleanly with existing patterns. The codebase already implements an identical owner-only feedback flow for `KnowledgeBaseQuestionLog` (entity → EF configuration → repository → MediatR handlers → controller endpoints → React Query hooks). The KB's `QuestionLoggingBehavior` is registered as a module-scoped `IPipelineBehavior` in `KnowledgeBaseModule.cs:58`, and the same pattern applies verbatim for `GenerateLeafletRequest/Response`. Integration points are well-defined:

- **MediatR pipeline**: `GenerateLeafletHandler` (Application/Features/Leaflet/UseCases/GenerateLeaflet) wraps in a new `LeafletGenerationLoggingBehavior` registered in `LeafletModule.cs`.
- **Persistence**: `ApplicationDbContext` already exposes `LeafletDocuments`/`LeafletChunks` DbSets; adding `LeafletGenerations` follows the same convention. `LeafletRepository` already implements `ILeafletRepository`; the new methods extend the same class.
- **Controller**: `LeafletController` already uses `BaseApiController.HandleResponse`, the `[Authorize]` + `LeafletUpload` policy, and MediatR dispatch — so new endpoints slot in directly.
- **Frontend**: `KnowledgeBaseSearchAskTab.tsx:56-163` contains the feedback form to be extracted; `useKnowledgeBase.ts:386-409` provides the 409→`alreadySubmitted` mutation pattern; `useLeaflet.ts` is missing only the new mutation/query.

The KB module is the canonical reference; deviating from it is unwarranted. **The spec is largely correct**; a few corrections are required (called out below) — most importantly, response/error-code typing and the controller's `Generate` action retains its current `try/catch` wrapper.

## Proposed Architecture

### Component Overview

```
HTTP request
   │
   ▼
┌──────────────────────────────────────────────────────────────┐
│ LeafletController                                            │
│   POST /api/leaflet/generate     → GenerateLeafletRequest    │
│   POST /api/leaflet/feedback     → SubmitLeafletFeedback…    │
│   GET  /api/leaflet/feedback/list → GetLeafletFeedbackList…  │
│   GET  /api/leaflet/generations/{id} → GetLeafletGeneration… │
└──────────────────────────────────────────────────────────────┘
   │ MediatR.Send
   ▼
┌──────────────────────────────────────────────────────────────┐
│ MediatR pipeline (per request type)                          │
│  GenerateLeafletRequest:                                     │
│    [global ValidationBehavior]                               │
│     → LeafletGenerationLoggingBehavior (NEW, scoped to module)│
│        → GenerateLeafletHandler                              │
│  SubmitLeafletFeedbackRequest:                               │
│    [global ValidationBehavior] → SubmitLeafletFeedbackHandler│
│  GetLeafletFeedbackListRequest:                              │
│    → GetLeafletFeedbackListHandler                           │
│  GetLeafletGenerationRequest:                                │
│    → GetLeafletGenerationHandler                             │
└──────────────────────────────────────────────────────────────┘
   │
   ▼
┌──────────────────────────────────────────────────────────────┐
│ ILeafletRepository (extended)                                │
│   + SaveGenerationAsync(LeafletGeneration)                   │
│   + GetGenerationByIdAsync(Guid)                             │
│   + GetGenerationsPagedAsync(filters, sort, paging)          │
│   + GetGenerationStatsAsync()                                │
│   + SaveChangesAsync()  ← new (LeafletRepository currently   │
│                            has SaveChangesAsync method only) │
└──────────────────────────────────────────────────────────────┘
   │
   ▼
┌──────────────────────────────────────────────────────────────┐
│ EF Core / PostgreSQL                                         │
│   public."LeafletGenerations" (NEW)                          │
└──────────────────────────────────────────────────────────────┘

Frontend:
LeafletGenerator page
  └── LeafletResult ── RagFeedbackForm (NEW shared component)
                          ↑ also used by
KnowledgeBaseSearchAskTab ┘
                          │
                  useSubmitLeafletFeedbackMutation (NEW)
                  useLeafletFeedbackListQuery (NEW)
```

### Key Design Decisions

#### Decision 1: Response/error-code typing follows the existing `BaseResponse(ErrorCodes, ...)` constructor
**Options considered:**
- (a) The spec/brief shows `SubmitLeafletFeedbackResponse(string errorCode, ...)` — would diverge from the rest of the codebase.
- (b) Use the existing `BaseResponse(ErrorCodes errorCode, Dictionary<string, string>?)` constructor — matches every other handler.

**Chosen approach:** (b). All response classes in `Anela.Heblo.Application/Shared` use the `ErrorCodes` enum. `BaseApiController.HandleResponse` reflects on the enum's `[HttpStatusCode]` attribute to map to HTTP. Using a string would silently bypass that mapping and break Conflict (409) → the frontend's 409→`alreadySubmitted` contract.

**Rationale:** The brief's signature is wrong; `SubmitFeedbackResponse` (KB) and every existing response use `ErrorCodes`. Following convention is mandatory for proper status-code mapping.

#### Decision 2: Keep `LeafletController.Generate`'s existing `try/catch` wrapper as-is
**Options considered:**
- (a) Restructure to `HandleResponse(result)` for consistency with new endpoints.
- (b) Leave the existing `try/Ok/UnprocessableEntity/502` handling in place.

**Chosen approach:** (b). The Generate endpoint catches `EmptyRetrievalException` (422) and unhandled exceptions (502); these are pre-existing semantics from Phase 1.

**Rationale:** `LeafletGenerationLoggingBehavior` swallows its own persistence errors (per FR-1), so they never propagate to the controller's catch. The controller's existing logic is orthogonal to logging concerns. Don't refactor unrelated code per CLAUDE.md "surgical changes" rule.

#### Decision 3: Pipeline ordering — register `LeafletGenerationLoggingBehavior` after MediatR is configured
**Options considered:**
- (a) Register in `LeafletModule.AddLeafletModule` (mirrors KB's `KnowledgeBaseModule.cs:58`).
- (b) Register globally in `Program.cs` alongside `ValidationBehavior`.

**Chosen approach:** (a). MediatR runs registered behaviors in registration order; the global `ValidationBehavior` is registered first (in API startup), then the module-scoped logging behavior is appended. This means validation runs before logging — the desired order.

**Rationale:** Module-scoping confines the logging concern to the Leaflet module and exactly mirrors `QuestionLoggingBehavior` registration.

#### Decision 4: `RagFeedbackForm` ownership of state
**Options considered:**
- (a) Internalize submission state machine (`idle`/`submitted`/`alreadySubmitted`) inside the component, accept only the mutation function.
- (b) Lift the state machine to the parent; component receives `alreadySubmitted`, `isSuccess`, `isSubmitting`, `onSubmit` props (per spec FR-5).

**Chosen approach:** (b). The component owns input state (`precisionScore`, `styleScore`, `comment`) only. The parent (KB tab and `LeafletResult`) tracks the post-submit state, because it knows which mutation to use and whether to refresh queries (e.g., the leaflet manager list).

**Rationale:** Keeps the shared component truly hook-agnostic. Parents wrap their own mutation hooks (`useSubmitFeedbackMutation` for KB, `useSubmitLeafletFeedbackMutation` for leaflet) and translate the result into props. Matches the spec's explicit prop contract.

#### Decision 5: Defensive null check on `currentUser.Id` in `SubmitLeafletFeedbackHandler`
**Options considered:**
- (a) Mirror KB exactly: `if (generation.UserId != currentUser.Id) return Forbidden`.
- (b) Add an explicit guard: if `currentUser?.Id` is null or empty, return `Forbidden` before the equality check.

**Chosen approach:** (b). When both `generation.UserId` and `currentUser.Id` are null, the equality check passes and an anonymous (or system) caller could pass owner-only authorization. The `[Authorize]` attribute already prevents anonymous calls, so this is defense-in-depth, but the cost is one line.

**Rationale:** Cheap hardening against environments where `BypassJwtValidation` or mock auth could yield null user ids. Not a regression of KB behavior — KB has the same theoretical bug; we don't have to propagate it.

## Implementation Guidance

### Directory / Module Structure

```
backend/src/Anela.Heblo.Domain/Features/Leaflet/
  LeafletGeneration.cs                                     [NEW] entity
  LeafletFeedbackStats.cs                                  [NEW] record
  ILeafletRepository.cs                                    [EDIT] add 5 methods

backend/src/Anela.Heblo.Persistence/Features/Leaflet/
  LeafletGenerationConfiguration.cs                        [NEW] EF config
  LeafletRepository.cs                                     [EDIT] implement 5 methods

backend/src/Anela.Heblo.Persistence/
  ApplicationDbContext.cs                                  [EDIT] add DbSet
  Migrations/<timestamp>_AddLeafletGenerations.cs          [NEW] auto-generated

backend/src/Anela.Heblo.Application/Features/Leaflet/
  LeafletModule.cs                                         [EDIT] register behavior
  Pipeline/                                                [NEW DIR]
    LeafletGenerationLoggingBehavior.cs                    [NEW]
  UseCases/GenerateLeaflet/
    GenerateLeafletResponse.cs                             [EDIT] +Id +KbSourceCount +LeafletSourceCount
    GenerateLeafletHandler.cs                              [EDIT] populate counts on response
  UseCases/SubmitLeafletFeedback/                          [NEW DIR]
    SubmitLeafletFeedbackRequest.cs                        [NEW]
    SubmitLeafletFeedbackResponse.cs                       [NEW]
    SubmitLeafletFeedbackHandler.cs                        [NEW]
  UseCases/GetLeafletFeedbackList/                         [NEW DIR]
    GetLeafletFeedbackListRequest.cs                       [NEW]
    GetLeafletFeedbackListResponse.cs                      [NEW]   (incl. LeafletFeedbackSummary, LeafletFeedbackStatsDto)
    GetLeafletFeedbackListHandler.cs                       [NEW]
  UseCases/GetLeafletGeneration/                           [NEW DIR]
    GetLeafletGenerationRequest.cs                         [NEW]
    GetLeafletGenerationResponse.cs                        [NEW]
    GetLeafletGenerationHandler.cs                         [NEW] mirrors GetArticleHandler

backend/src/Anela.Heblo.Application/Shared/
  ErrorCodes.cs                                            [EDIT] add 2502, 2503

backend/src/Anela.Heblo.API/Controllers/
  LeafletController.cs                                     [EDIT] add 3 endpoints

backend/test/Anela.Heblo.Tests/Features/Leaflet/
  SubmitLeafletFeedbackHandlerTests.cs                     [NEW]
  GetLeafletFeedbackListHandlerTests.cs                    [NEW]
  LeafletGenerationLoggingBehaviorTests.cs                 [NEW]
  LeafletRepositoryIntegrationTests.cs                     [NEW]

frontend/src/components/feedback/
  RagFeedbackForm.tsx                                      [NEW]

frontend/src/api/hooks/
  useLeaflet.ts                                            [EDIT] add hooks + types
  useKnowledgeBase.ts                                      [no change — existing mutation already 409-aware]

frontend/src/components/knowledge-base/
  KnowledgeBaseSearchAskTab.tsx                            [EDIT] use RagFeedbackForm

frontend/src/features/leaflet-generator/
  LeafletResult.tsx                                        [EDIT] add generationId prop + form
  (parent that renders LeafletResult)                      [EDIT] thread response.id through

frontend/src/i18n.ts                                       [EDIT] add 2 strings (cs + en)

frontend/src/api/hooks/__tests__/
  useLeaflet.test.ts                                       [NEW]
frontend/src/components/feedback/__tests__/
  RagFeedbackForm.test.tsx                                 [NEW]
```

### Interfaces and Contracts

**`ErrorCodes` enum additions** (Leaflet module range 25XX, next-sequential after `LeafletChunkNotFound = 2501`):

```csharp
[HttpStatusCode(HttpStatusCode.NotFound)]
LeafletFeedbackNotFound = 2502,
[HttpStatusCode(HttpStatusCode.Conflict)]
LeafletFeedbackAlreadySubmitted = 2503,
```

The `[HttpStatusCode]` attribute is essential — `BaseApiController.HandleResponse` reads it via reflection to map to HTTP status. **The frontend hook depends on Conflict (409) for the `alreadySubmitted` branch.**

**`SubmitLeafletFeedbackResponse` constructor must use `ErrorCodes`, not `string`:**

```csharp
public class SubmitLeafletFeedbackResponse : BaseResponse
{
    public SubmitLeafletFeedbackResponse() { }
    public SubmitLeafletFeedbackResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
```

The brief and spec both show `string errorCode` — that is **wrong**. All other response classes use `ErrorCodes`.

**`ILeafletRepository` additions** (placed at the end of the interface; existing `SaveChangesAsync(CancellationToken)` is already present — do not duplicate it):

```csharp
Task SaveGenerationAsync(LeafletGeneration generation, CancellationToken ct = default);
Task<LeafletGeneration?> GetGenerationByIdAsync(Guid id, CancellationToken ct = default);
Task<(IReadOnlyList<LeafletGeneration> Items, int TotalCount)> GetGenerationsPagedAsync(
    bool? hasFeedback, string? userId, string sortBy, bool descending,
    int page, int pageSize, CancellationToken ct = default);
Task<LeafletFeedbackStats> GetGenerationStatsAsync(CancellationToken ct = default);
// SaveChangesAsync already exists in ILeafletRepository — do NOT redeclare.
```

**`GenerateLeafletResponse` additions:**

```csharp
public class GenerateLeafletResponse : BaseResponse
{
    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("id")]
    public Guid? Id { get; set; }                    // NEW — set by behavior

    [JsonPropertyName("kbSourceCount")]
    public int KbSourceCount { get; set; }           // NEW — set by handler

    [JsonPropertyName("leafletSourceCount")]
    public int LeafletSourceCount { get; set; }      // NEW — set by handler
}
```

`GenerateLeafletHandler.Handle` must populate `KbSourceCount = kbHits.Count` and `LeafletSourceCount = leafletHits.Count` on the returned response so the behavior can read them after `next()`.

**`RagFeedbackForm` prop contract** (must work for both KB and Leaflet):

```typescript
interface RagFeedbackFormProps {
  onSubmit: (data: { precisionScore: number; styleScore: number; comment: string }) => void;
  isSubmitting: boolean;
  alreadySubmitted: boolean;
  isSuccess: boolean;
  errorMessage?: string;  // optional — when mutation fails for non-409 reason
}
```

**Frontend types added to `useLeaflet.ts`** mirror `useKnowledgeBase.ts` shapes (`LeafletFeedbackLogSummary`, `LeafletFeedbackStatsDto`, `GetLeafletFeedbackListParams`, etc.) — the OpenAPI generator will produce server-side names; the hand-written hooks should match them.

### Data Flow

**Generate → log row:**
```
Client POST /api/leaflet/generate
  → MediatR.Send(GenerateLeafletRequest)
    → ValidationBehavior (global)
      → LeafletGenerationLoggingBehavior.Handle (NEW)
        Stopwatch start
        var response = await next();           // GenerateLeafletHandler runs
        Stopwatch stop
        if (!response.Success) return response;  // skip log on failure
        try {
          insert LeafletGeneration {
            Topic, Audience.ToString(), Length.ToString(),
            FinalMarkdown=response.Content,
            KbSourceCount=response.KbSourceCount,
            LeafletSourceCount=response.LeafletSourceCount,
            DurationMs=sw.ElapsedMilliseconds,
            CreatedAt=DateTimeOffset.UtcNow,
            UserId=currentUserService.GetCurrentUser().Id
          };
          response.Id = generation.Id;
        } catch (Exception ex) {
          logger.LogError(ex, "Failed to log leaflet generation. Topic: {Topic}", request.Topic);
          // response.Id remains null — generation still returns to client
        }
        return response;
  ← controller returns Ok(response) with Id, Content, counts
```

**Submit feedback (one-shot, owner-only):**
```
Client POST /api/leaflet/feedback {generationId, precisionScore, styleScore, comment?}
  → MediatR pipeline (validation runs first via data annotations)
    → SubmitLeafletFeedbackHandler
      gen = repo.GetGenerationByIdAsync(generationId)
      if gen == null → BaseResponse(LeafletFeedbackNotFound) → 404
      currentUser = currentUserService.GetCurrentUser()
      if string.IsNullOrEmpty(currentUser.Id) → Forbidden  (defensive)
      if gen.UserId != currentUser.Id → Forbidden → 403
      if gen.PrecisionScore is not null OR gen.StyleScore is not null
         → LeafletFeedbackAlreadySubmitted → 409
      gen.PrecisionScore = req.PrecisionScore;
      gen.StyleScore     = req.StyleScore;
      gen.FeedbackComment = req.Comment;
      await repo.SaveChangesAsync(ct);
      return success
  ← BaseApiController.HandleResponse maps to HTTP status via ErrorCodes attributes
```

**Frontend submit (RagFeedbackForm in LeafletResult):**
```
User clicks "Submit"
  → useSubmitLeafletFeedbackMutation.mutate({generationId, precision, style, comment})
    → POST /api/leaflet/feedback
    if status 409: return { alreadySubmitted: true }
    if !response.ok: throw
    else return {}
  → onSuccess(result) in LeafletResult sets parent state:
       result.alreadySubmitted ? setFeedbackState('alreadySubmitted')
                               : setFeedbackState('submitted');
  RagFeedbackForm receives alreadySubmitted/isSuccess as props and shows appropriate banner.
```

**Manager list:**
```
GET /api/leaflet/feedback/list?hasFeedback=&userId=&sortBy=&...
  → [Authorize(LeafletUpload)]
  → GetLeafletFeedbackListHandler
    pageNumber = max(1, request.PageNumber)
    pageSize   = AllowedPageSizes.Contains(req) ? req : 20
    sortBy     = AllowedSortColumns.Contains(req) ? req : "CreatedAt"
    (logs, total) = repo.GetGenerationsPagedAsync(...)
    stats        = repo.GetGenerationStatsAsync()
    map to response DTOs
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Spec/brief specify `string errorCode` for response constructors — would silently break HTTP status mapping (`HandleResponse` only knows `ErrorCodes`) | **HIGH** | Use `ErrorCodes` enum; verify with a test that 409 is returned on the second submit. |
| `[ProducesResponseType(typeof(ProblemDetails), 409)]` in the brief is wrong — actual body is `SubmitLeafletFeedbackResponse` (BaseResponse) | LOW | Use `[ProducesResponseType(typeof(SubmitLeafletFeedbackResponse), 409)]` or omit the 409 attribute entirely (KB controller omits it and works). |
| Pipeline ordering: behavior must run after `ValidationBehavior`. If `ValidationBehavior` is registered after the new logging behavior, validation errors would be logged as successful generations. | MEDIUM | `ValidationBehavior` is registered globally in `Program.cs` at API startup, before module behaviors are added. Add a unit test that exercises invalid `Topic` (empty) and asserts no `LeafletGeneration` row is written. |
| `currentUser.Id` may be null in mock-auth or bypass-JWT environments → owner-only check passes for anonymous callers | LOW | Add explicit `string.IsNullOrEmpty(currentUser?.Id)` guard in `SubmitLeafletFeedbackHandler` returning `Forbidden`. |
| `SaveGenerationAsync` calls `SaveChangesAsync` internally — if the parent request had pending unrelated changes in the same scoped DbContext, they'd be flushed too. | LOW | Inspect `GenerateLeafletHandler` — it does no writes (only reads). Safe in current state. Add a comment in the behavior noting the assumption. |
| Race: two concurrent feedback submissions for the same generation. The "already submitted" check is read-then-write; both could pass the check before either persists. | LOW | At expected scale (single user, single tab), negligible. If observed in practice, wrap in a single `UPDATE … WHERE PrecisionScore IS NULL AND StyleScore IS NULL` and check affected rows. **Do not implement now.** |
| Migration auto-application disabled per project convention; ops must run `dotnet ef database update` manually post-deploy | MEDIUM | Document in PR description; reference `docs/architecture/infrastructure.md`. |
| OpenAPI client regeneration: new DTOs must be **classes**, not records (project rule) | MEDIUM | Verified — `LeafletFeedbackSummary`, `LeafletFeedbackStatsDto`, request/response DTOs are all classes per spec. The internal domain `LeafletFeedbackStats` is a `record` (not exposed via API) — that's fine. |
| `Forbidden` from KB-style `Forbid()` returns 403 with no body; frontend hook would throw. UX: user is owner so this should never happen, but a hostile call would surface a generic error. | LOW | Acceptable. The mutation hook only treats 409 specially; 403 throws like any other failure. The only path that can hit 403 requires guessing another user's `generationId` — out of normal UX flow. |
| Pipeline behavior throws after `next()` succeeds — controller's `try/catch` would catch it and return 502 even though generation succeeded | MEDIUM | The behavior already wraps the persistence in `try/catch` (per FR-1). Add a unit test asserting an exception inside the persistence call still returns the generation response (not rethrown). |

## Specification Amendments

1. **Use `ErrorCodes` enum, not strings, in `SubmitLeafletFeedbackResponse` constructor.** Fix snippet in spec FR-2 / brief Step 9. Required for `BaseApiController.HandleResponse` mapping.
2. **Add `[HttpStatusCode]` attributes** to the new `ErrorCodes`: `LeafletFeedbackNotFound = 2502` → `NotFound`; `LeafletFeedbackAlreadySubmitted = 2503` → `Conflict`. Existing `LeafletChunkNotFound = 2501` already occupies the 25XX range; assign 2502 / 2503 next.
3. **Defensive null guard in `SubmitLeafletFeedbackHandler`**: when `currentUser?.Id` is null/empty, short-circuit to `Forbidden` before the equality check.
4. **`GenerateLeafletHandler` must populate** `response.KbSourceCount` and `response.LeafletSourceCount` from `kbHits.Count` and `leafletHits.Count` before returning. The behavior reads these after `next()`.
5. **Do not redeclare `SaveChangesAsync` in `ILeafletRepository`.** It already exists (verified — `ILeafletRepository.cs:23`).
6. **Keep `LeafletController.Generate`'s existing `try/catch`.** Do not refactor to `HandleResponse`. The new endpoints (feedback POST/GET, generation lookup) use `HandleResponse` per existing convention.
7. **Drop the `[ProducesResponseType(typeof(ProblemDetails), 409)]` line** in the spec's controller snippet — the body type is `SubmitLeafletFeedbackResponse`, not `ProblemDetails`. Either omit the attribute (matches KB) or use the correct type.
8. **`RagFeedbackForm` owns input state only.** State machine (`idle`/`submitted`/`alreadySubmitted`) lives in the parent (`KnowledgeBaseSearchAskTab`, `LeafletResult`), driven by the mutation result. Spec FR-5 is already correct on this — the brief Step 12 is also correct.
9. **Test addition**: pipeline behavior runs after validation. Unit test: empty `Topic` → validation fails → no log row written.
10. **Test addition**: behavior swallows persistence exceptions. Unit test with mocked `ILeafletRepository.SaveGenerationAsync` throwing → response returned with `Id == null`, error logged, no rethrow.

## Prerequisites

- **Branch**: `feature/genai_consistency` exists at `origin/feature/genai_consistency` (verified). New work branches off it; PR target = `feature/genai_consistency`, **not** `main`.
- **Phase 1 merged**: leaflet generator routes, sidebar, `GenerateLeafletHandler/Request/Response`, `AudienceType`, `LeafletLength` must be on `feature/genai_consistency`.
- **No new packages**: MediatR, EF Core 8, Pgvector, React Query — all already present.
- **Manual migration**: After merge, ops runs `dotnet ef database update --project backend/src/Anela.Heblo.Persistence --startup-project backend/src/Anela.Heblo.API`. Document in the PR.
- **Authorization policy `LeafletUpload`** is already wired in `AuthorizationConstants.cs:67` and applied to existing leaflet endpoints — no additional setup.
- **`ICurrentUserService`** is already DI-registered and used by `QuestionLoggingBehavior` and KB feedback handler — no additional setup.
- **OpenAPI client regeneration**: the TypeScript client regenerates on `npm run build`. The new DTOs (classes, not records) will surface as TS interfaces automatically; the new hand-written hooks in `useLeaflet.ts` should match the generated names.