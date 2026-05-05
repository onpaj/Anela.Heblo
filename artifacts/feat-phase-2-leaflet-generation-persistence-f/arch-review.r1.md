# Architecture Review: Leaflet Generation Persistence + Feedback (Phase 2)

## Architectural Fit Assessment

The feature is a clean port of the established KnowledgeBase Q&A persistence pattern (`KnowledgeBaseQuestionLog` + `QuestionLoggingBehavior` + `SubmitFeedbackHandler` + `GetFeedbackListHandler`) into the Leaflet feature slice. Every primitive the spec calls for already exists and is in active use:

- **MediatR pipeline behavior** registered per-request-type in module `AddXxxModule` (`KnowledgeBaseModule.cs:58`).
- **`BaseResponse` + `ErrorCodes` enum + `[HttpStatusCode]` attribute** drive HTTP envelope mapping in `BaseApiController.HandleResponse` (`BaseApiController.cs:28`). Adding two new entries to `ErrorCodes` is the entire backend "wiring" — controller code does not need to know about HTTP codes.
- **`ICurrentUserService.GetCurrentUser().Id`** is the canonical owner-id source (`SubmitFeedbackHandler.cs:34`, `QuestionLoggingBehavior.cs:37`).
- **`AuthorizationConstants.Policies.LeafletUpload`** is already defined and used on `LeafletController` (`LeafletController.cs:105,113`) — gating `feedback/list` with it is consistent.
- **Single shared `ApplicationDbContext`** with `ApplyConfigurationsFromAssembly` (`ApplicationDbContext.cs:117`) — adding `LeafletGenerationConfiguration` requires no extra registration.
- **Repository pattern**: `ILeafletRepository` already exists with chunk/document operations and a `SaveChangesAsync`. Extending it (rather than introducing a new `ILeafletGenerationRepository`) matches the KB precedent where `IKnowledgeBaseRepository` mixes document, chunk, and question-log operations.

**Integration points:** four — `ApplicationDbContext` (DbSet), `ILeafletRepository` (5 new methods), `LeafletModule` (one `AddScoped` for the pipeline behavior), `LeafletController` (3 new endpoints). All zero-risk additions. The only modification to existing code is `GenerateLeafletResponse` (3 new properties) and `GenerateLeafletHandler` (populate two source-count fields from the lists it already builds).

**No conflicts** with the KB feature, the existing leaflet feature, or shared infrastructure. The pattern parallel between KB and Leaflet is now strong enough that a "RAG generation logging" abstraction could be considered later — see Risks/Mitigations.

## Proposed Architecture

### Component Overview

```
                          ┌────────────────────────────────────┐
HTTP POST /api/leaflet/   │          LeafletController         │
   generate    ──────────▶│  Generate (existing)               │
   feedback    ──────────▶│  SubmitFeedback (new)              │
   feedback/list ────────▶│  GetFeedbackList (new, policy:    │
   generations/{id} ─────▶│  LeafletUpload)                    │
                          │  GetGeneration (new)               │
                          └────────────┬───────────────────────┘
                                       │ IMediator.Send
                                       ▼
                          ┌─────────────────────────────────────┐
                          │   MediatR pipeline                  │
                          │   ValidationBehavior (global)       │
                          │   LeafletGenerationLoggingBehavior  │
                          │     (scoped to                      │
                          │      GenerateLeafletRequest)        │
                          └────────────┬────────────────────────┘
                                       ▼
                          ┌─────────────────────────────────────┐
                          │   Handlers                          │
                          │   GenerateLeafletHandler (existing) │
                          │   SubmitLeafletFeedbackHandler      │
                          │   GetLeafletFeedbackListHandler     │
                          │   GetLeafletGenerationHandler       │
                          └────────────┬────────────────────────┘
                                       ▼
                          ┌─────────────────────────────────────┐
                          │   ILeafletRepository (extended)     │
                          │   SaveGenerationAsync               │
                          │   GetGenerationByIdAsync            │
                          │   GetGenerationsPagedAsync          │
                          │   GetGenerationStatsAsync           │
                          │   SaveChangesAsync (existing)       │
                          └────────────┬────────────────────────┘
                                       ▼
                          ┌─────────────────────────────────────┐
                          │   ApplicationDbContext              │
                          │   DbSet<LeafletGeneration>          │
                          │   table public."LeafletGenerations" │
                          │   indexes: CreatedAt, UserId,       │
                          │            PrecisionScore filtered  │
                          └─────────────────────────────────────┘

Frontend
  features/leaflet-generator/
    LeafletGenerateTab ──── owns generationId state ─┐
    LeafletResult       ◀── content + generationId ──┘
                            │
                            ▼
  components/feedback/
    RagFeedbackForm (generic, prop-driven)
                            ▲
                            │
  components/knowledge-base/
    KnowledgeBaseSearchAskTab ── reuses RagFeedbackForm

  api/hooks/useLeaflet.ts
    useSubmitLeafletFeedbackMutation (POST, raw fetch, 409 → alreadySubmitted)
    useLeafletFeedbackListQuery (GET)
```

### Key Design Decisions

#### Decision 1: Extend `ILeafletRepository` rather than introducing `ILeafletGenerationRepository`

**Options considered:**
- **A.** Add five new methods to `ILeafletRepository` (mirrors `IKnowledgeBaseRepository`, where document/chunk/question-log operations coexist).
- **B.** New `ILeafletGenerationRepository` interface, separate concrete class, separate DI registration.

**Chosen approach:** A — extend `ILeafletRepository`.

**Rationale:** The brief specifies it explicitly, the KB pattern (which we are deliberately mirroring) does the same, and `LeafletRepository` is already the only consumer of `ApplicationDbContext` for the leaflet feature. Splitting interfaces would add a class without solving any pain. The interface is currently 14 methods; adding 5 keeps it well below the 800-line ceiling and the methods are cohesive (they all operate on the leaflet-feature DbSets via the same `_context`). If the interface grows past ~25 methods in future phases, splitting becomes worthwhile.

#### Decision 2: Logging behavior writes only on `response.Success == true`

**Options considered:**
- **A.** Mirror KB's `QuestionLoggingBehavior` exactly — write the log unconditionally.
- **B.** Skip the write when `!response.Success` (per spec FR-1).

**Chosen approach:** B — skip on failure.

**Rationale:** This is a deliberate, documented divergence from KB. In the current `GenerateLeafletHandler`, `EmptyRetrievalException` (insufficient KB coverage) is thrown and never reaches the behavior, so in practice both options produce the same data today. But the spec is correct to future-proof: if the handler later returns `BaseResponse` failure envelopes (e.g., for "AI service unavailable"), we do not want incomplete `LeafletGeneration` rows polluting feedback stats. The KB handler should arguably adopt the same guard, but that is out of scope for Phase 2.

#### Decision 3: Reuse `ICurrentUserService.GetCurrentUser().Id` in the logging behavior, accept null

**Options considered:**
- **A.** Require authenticated user; throw if `currentUser.Id` is null.
- **B.** Tolerate null (write the row anyway with `UserId = null`).

**Chosen approach:** B — tolerate null.

**Rationale:** The controller is `[Authorize]`, so anonymous calls are blocked at the framework boundary; `currentUser.Id` should never be null in practice. The behavior already swallows all exceptions and logs them. Treating null as "anonymous" matches the schema (`UserId nvarchar(200) NULL`) and keeps the behavior simple. Owner checks in the feedback handler will fail-closed — a null `UserId` row cannot be matched by any authenticated submitter.

#### Decision 4: Single-generation GET inherits controller-level `[Authorize]` (no owner gate)

**Options considered:**
- **A.** Plain `[Authorize]` — any authenticated user can fetch any generation if they know the GUID.
- **B.** Owner-only: 403 if `generation.UserId != currentUser.Id`.
- **C.** Manager-or-owner: pass for owner OR `LeafletUpload` policy.

**Chosen approach:** A (per spec FR-4 and resolved Open Questions).

**Rationale:** Generation IDs are server-issued GUIDs and are not enumerable. Cross-user reads via this endpoint are not part of any UI flow today. If a future stakeholder needs visible cross-user view, the manager-gated `feedback/list` endpoint already covers that. **Mitigation if threat model tightens:** add owner-or-policy check in the handler — same pattern as `SubmitLeafletFeedbackHandler` — without API surface change.

#### Decision 5: `RagFeedbackForm` placed in `components/feedback/` rather than `components/shared/` or per-feature

**Options considered:**
- **A.** `components/feedback/RagFeedbackForm.tsx` (per spec FR-5).
- **B.** Keep duplicated inline forms; add second copy to leaflet feature folder.
- **C.** `components/shared/RagFeedbackForm.tsx`.

**Chosen approach:** A.

**Rationale:** "Feedback" is now a cross-feature concept (KB + Leaflet, plausibly Article in future). A dedicated `components/feedback/` folder communicates intent better than the broad `shared/`. Spec is explicit and aligns with the existing convention (`components/knowledge-base/`, `components/Layout/`).

## Implementation Guidance

### Directory / Module Structure

```
backend/src/Anela.Heblo.Domain/Features/Leaflet/
  LeafletGeneration.cs                        [new — entity]
  LeafletFeedbackStats.cs                     [new — sealed record]
  ILeafletRepository.cs                       [edit — add 4 methods]

backend/src/Anela.Heblo.Persistence/Features/Leaflet/
  LeafletGenerationConfiguration.cs           [new — IEntityTypeConfiguration]
  LeafletRepository.cs                        [edit — implement 4 methods]

backend/src/Anela.Heblo.Persistence/
  ApplicationDbContext.cs                     [edit — add DbSet]
  Migrations/{ts}_AddLeafletGenerations.cs    [new — generated]

backend/src/Anela.Heblo.Application/Features/Leaflet/
  LeafletModule.cs                            [edit — register pipeline behavior]
  Pipeline/
    LeafletGenerationLoggingBehavior.cs       [new]
  UseCases/
    GenerateLeaflet/
      GenerateLeafletResponse.cs              [edit — add Id, KbSourceCount, LeafletSourceCount]
      GenerateLeafletHandler.cs               [edit — populate source counts]
    SubmitLeafletFeedback/
      SubmitLeafletFeedbackRequest.cs         [new]
      SubmitLeafletFeedbackResponse.cs        [new]
      SubmitLeafletFeedbackHandler.cs         [new]
    GetLeafletFeedbackList/
      GetLeafletFeedbackListRequest.cs        [new]
      GetLeafletFeedbackListResponse.cs       [new — includes LeafletFeedbackSummary, LeafletFeedbackStatsDto]
      GetLeafletFeedbackListHandler.cs        [new]
    GetLeafletGeneration/
      GetLeafletGenerationRequest.cs          [new]
      GetLeafletGenerationResponse.cs         [new]
      GetLeafletGenerationHandler.cs          [new]

backend/src/Anela.Heblo.Application/Shared/
  ErrorCodes.cs                               [edit — add 2 codes under 25XX block]

backend/src/Anela.Heblo.API/Controllers/
  LeafletController.cs                        [edit — add 3 endpoints]

backend/test/Anela.Heblo.Tests/Features/Leaflet/
  Pipeline/LeafletGenerationLoggingBehaviorTests.cs       [new]
  UseCases/SubmitLeafletFeedbackHandlerTests.cs           [new]
  UseCases/GetLeafletFeedbackListHandlerTests.cs          [new]
  Persistence/LeafletRepositoryGenerationsTests.cs        [new — integration]

frontend/src/components/feedback/
  RagFeedbackForm.tsx                         [new]
  __tests__/RagFeedbackForm.test.tsx          [new]

frontend/src/components/knowledge-base/
  KnowledgeBaseSearchAskTab.tsx               [edit — replace inline form]

frontend/src/features/leaflet-generator/
  LeafletGenerateTab.tsx                      [edit — track generationId]
  LeafletResult.tsx                           [edit — accept generationId, render form]
  __tests__/LeafletResult.test.tsx            [edit]

frontend/src/api/hooks/
  useLeaflet.ts                               [edit — add 2 hooks + types + query keys]
  __tests__/useLeaflet.test.ts                [new — 409 conversion]

frontend/src/i18n.ts                          [edit — 2 new translations cs/en]
```

### Interfaces and Contracts

**`ILeafletRepository` additions** (place after `SaveChangesAsync`, mirroring the order in `IKnowledgeBaseRepository`):

```csharp
Task SaveGenerationAsync(LeafletGeneration generation, CancellationToken ct = default);
Task<LeafletGeneration?> GetGenerationByIdAsync(Guid id, CancellationToken ct = default);
Task<(IReadOnlyList<LeafletGeneration> Items, int TotalCount)> GetGenerationsPagedAsync(
    bool? hasFeedback, string? userId, string sortBy, bool descending,
    int page, int pageSize, CancellationToken ct = default);
Task<LeafletFeedbackStats> GetGenerationStatsAsync(CancellationToken ct = default);
```

`SaveChangesAsync` already exists — do not duplicate.

**`LeafletGeneration` entity:** mutable class (matches KB `KnowledgeBaseQuestionLog`) — feedback fields are updated in-place by the handler before `SaveChangesAsync`. This is the only place in the leaflet feature that holds tracked entity state long enough to mutate, and EF needs that for the dirty check. Domain-style "with" returns are not appropriate here.

**`LeafletFeedbackStats`:** `sealed record` with 4 properties (matches KB `FeedbackAggregateStats` shape but is in `Domain.Features.Leaflet`). Internal type, not crossing the OpenAPI boundary, so `record` is fine.

**Response DTOs** (`SubmitLeafletFeedbackResponse`, `GetLeafletFeedbackListResponse`, `GetLeafletGenerationResponse`, `LeafletFeedbackSummary`, `LeafletFeedbackStatsDto`): all **classes**, all derive from `BaseResponse` where applicable, all with public auto-properties — per project convention "DTOs are classes, never records".

**`GenerateLeafletResponse` extension** (existing class, append properties):

```csharp
public class GenerateLeafletResponse : BaseResponse
{
    [JsonPropertyName("content")] public string Content { get; set; } = string.Empty;
    public Guid? Id { get; set; }              // populated by behavior on success
    public int KbSourceCount { get; set; }     // populated by handler
    public int LeafletSourceCount { get; set; }// populated by handler
}
```

**`ErrorCodes` additions** (insert under "Leaflet module errors (25XX)" block):

```csharp
[HttpStatusCode(HttpStatusCode.NotFound)]
LeafletFeedbackNotFound = 2502,
[HttpStatusCode(HttpStatusCode.Conflict)]
LeafletFeedbackAlreadySubmitted = 2503,
```

`Forbidden` (0014) already exists. `BaseApiController.HandleResponse` will map these correctly without controller changes — the controller calls `HandleResponse(result)` and the `[HttpStatusCode]` attribute drives the response code.

**Frontend hook contract:**

```typescript
interface SubmitLeafletFeedbackRequest {
  generationId: string;
  precisionScore: number;
  styleScore: number;
  comment?: string;
}

interface SubmitLeafletFeedbackResult {
  alreadySubmitted?: true;
}

useSubmitLeafletFeedbackMutation():
    UseMutationResult<SubmitLeafletFeedbackResult, Error, SubmitLeafletFeedbackRequest>
```

Implementation: raw `fetch` against `${apiClient.baseUrl}/api/leaflet/feedback`, identical structure to `useSubmitFeedbackMutation` in `useKnowledgeBase.ts:386`. **Do NOT use the auto-generated `client.leaflet_*` method** — the existing `useLeaflet.ts` hooks all use raw fetch with `apiClient.baseUrl`, and the 409→`alreadySubmitted` translation requires inspecting `response.status` before `response.ok`. The OpenAPI client will throw on 409 and lose the discrimination.

**`RagFeedbackForm` props:**

```typescript
interface RagFeedbackFormProps {
  onSubmit: (data: { precisionScore: number; styleScore: number; comment: string }) => void;
  isSubmitting: boolean;
  alreadySubmitted: boolean;
  isSuccess: boolean;
}
```

No KB- or Leaflet-specific imports inside the component. The container (KB tab or `LeafletResult`) owns the mutation hook and translates its state into these four props.

### Data Flow

**Successful generation → log → feedback (happy path):**

```
1. Client POST /api/leaflet/generate { topic, audience, length }
2. ValidationBehavior validates request DTO (Range, Required, MaxLength).
3. LeafletGenerationLoggingBehavior:
   a. starts Stopwatch
   b. calls next() → GenerateLeafletHandler runs (KB+Leaflet retrieval, 2× LLM)
   c. on Success: builds LeafletGeneration { Id=NewGuid, Topic, Audience.ToString(),
      Length.ToString(), FinalMarkdown=Content, KbSourceCount, LeafletSourceCount,
      DurationMs=sw.Elapsed, CreatedAt=UtcNow, UserId=currentUser.Id }
   d. _repository.SaveGenerationAsync(generation, ct) → INSERT + SaveChanges
   e. response.Id = generation.Id
   f. returns response (now Content, Id, KbSourceCount, LeafletSourceCount populated)
4. Controller returns 200 with envelope.
5. Frontend stores response.id in component state, passes generationId to LeafletResult.
6. RagFeedbackForm renders below Copy/Regenerate buttons.

7. User picks scores + comment → POST /api/leaflet/feedback { generationId, ... }
8. ValidationBehavior validates request (Range 1–5, MaxLength 1000).
9. SubmitLeafletFeedbackHandler:
   - GetGenerationByIdAsync → null? return 404 envelope LeafletFeedbackNotFound
   - generation.UserId != currentUser.Id? return 403 envelope Forbidden
   - generation.PrecisionScore != null OR StyleScore != null?
       return 409 envelope LeafletFeedbackAlreadySubmitted
   - mutate generation (set 3 fields), SaveChangesAsync.
10. Controller returns 200; frontend marks form submitted.
```

**Logging failure (defensive path):**

```
3'. SaveGenerationAsync throws (e.g., DB unavailable)
   → caught by behavior, ILogger.LogError(ex, ...)
   → response returned WITHOUT Id set (Id remains null)
4'. Controller returns 200 with full content but no Id.
5'. Frontend: response.id is null → RagFeedbackForm not rendered.
   User keeps the markdown; only the feedback signal is lost.
```

**Manager view of all generations:**

```
GET /api/leaflet/feedback/list?hasFeedback=true&pageSize=20
  → [Authorize(Policy = LeafletUpload)] → 403 if user lacks leaflet_manager role
  → Handler validates pageSize ∈ {10,20,50}, sortBy ∈ {CreatedAt, PrecisionScore, StyleScore}
  → repository runs filter + paged query (uses CreatedAt index for default sort)
  → repository runs 4 aggregate queries for stats
  → returns { Logs, TotalCount, PageNumber, PageSize, Stats }
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Cross-user read of another user's generation via known GUID on `GET /generations/{id}` | LOW | Spec accepts; mitigate later by adding owner-or-`LeafletUpload`-policy guard inside the handler if threat model tightens. No API change required. |
| `FinalMarkdown` is unbounded text — list endpoint payload can grow large with `pageSize=50` | MEDIUM | Page size capped at 50; `text` column compresses well in pgsql. If page response > 1 MB becomes routine, add a `Truncate(FinalMarkdown, 500)` or split list vs detail (already have GET-by-id). Acceptable at MVP scale. |
| `GetGenerationStatsAsync` runs four full-table aggregates per list page request | LOW | Single table, one indexed column (`PrecisionScore`). At MVP scale (< 100k rows) cost is negligible; matches KB pattern which does the same. Add memoization (5-min cache) if row count grows past 1M or list page becomes hot. |
| Logging behavior swallows all exceptions — silent data loss on persistent DB outage | LOW | `ILogger.LogError` emits structured logs; ops alerting on repeated failures will surface it. Generation success-rate is the user-visible signal, not log-write-rate. Spec explicitly chooses availability over completeness here. |
| `LeafletGenerationLoggingBehavior` writes UTC `DateTimeOffset` but `ApplicationDbContext.OnModelCreating` does DateTime↔Unspecified conversion (not DateTimeOffset) | LOW | `DateTimeOffset` columns are unaffected by the conversion loop in `ApplicationDbContext.cs:120–143` (it only matches `DateTime`/`DateTime?`). Matches KB pattern which uses `DateTimeOffset` successfully. Verify with integration test. |
| Two concurrent feedback submits could both pass the "no feedback yet" check before either commits | LOW | One-shot guard is racy in theory but the UI immediately disables the form after first click; second click would have to come from a second browser tab. Spec accepts last-write-wins; concurrency token explicitly out of scope. If observed in practice, add a `[ConcurrencyCheck]` on `PrecisionScore` or use `UPDATE ... WHERE PrecisionScore IS NULL`. |
| `GenerateLeafletHandler` currently throws `EmptyRetrievalException` instead of returning a failure envelope, so `if (!response.Success)` early return in the new behavior is unreachable today | LOW | Code is correct and future-proof; document this so a reader does not "simplify" the guard away. Add a unit test that sets `response.Success=false` to lock the contract. |
| Pattern duplication between KB `QuestionLoggingBehavior` and new `LeafletGenerationLoggingBehavior` invites drift | MEDIUM | Accept duplication for Phase 2 (small, structurally identical). After Phase 2 ships, evaluate extracting `IRagGenerationLog` / `IRagLogStore<TLog>` abstractions across both features. Premature today (one duplication is not three). |
| Migration name collision if another developer's branch also adds a migration for Phase 2 of any feature | LOW | Solo developer per CLAUDE.md. Coordinate via the integration branch `feature/genai_consistency` — rebase before generating the migration. |

## Specification Amendments

1. **`ErrorCodes` numeric assignments must be specified.** The spec names the codes but not the numeric values. Use `LeafletFeedbackNotFound = 2502` and `LeafletFeedbackAlreadySubmitted = 2503` (next free slots in the 25XX leaflet block, after the existing `LeafletChunkNotFound = 2501`). Both must carry `[HttpStatusCode(HttpStatusCode.NotFound)]` and `[HttpStatusCode(HttpStatusCode.Conflict)]` respectively — without the attributes, `BaseApiController.HandleResponse` defaults to 400 and the 404/409 contract breaks.

2. **`SubmitLeafletFeedbackResponse` constructor signature** must take `ErrorCodes` enum, not `string`. The brief snippet writes `(string errorCode, …)` but `BaseResponse(ErrorCodes errorCode, Dictionary<string,string>?)` is the only matching base ctor (`BaseResponse.cs:34`). Mirror `SubmitFeedbackResponse` (KB) verbatim.

3. **`GenerateLeafletResponse` modification scope.** The spec lists `Id` only in Step 6 but FR-1 also requires `KbSourceCount` and `LeafletSourceCount` to be populated. Make the response shape change explicit: add three properties simultaneously. `GenerateLeafletHandler` populates the two counts (it already has `kbHits` and `leafletHits` lists at lines 55–61); the behavior populates `Id`.

4. **The new `LeafletRepository.SaveGenerationAsync` should call `SaveChangesAsync` internally**, matching `KnowledgeBaseRepository.SaveQuestionLogAsync` (`KnowledgeBaseRepository.cs:244`). The brief shows this; the spec FR text is silent. Confirm the convention.

5. **`useSubmitLeafletFeedbackMutation` must use raw `fetch`** with `${apiClient.baseUrl}/api/leaflet/feedback` and inspect `response.status === 409` BEFORE `response.ok`, exactly as `useSubmitFeedbackMutation` does (`useKnowledgeBase.ts:386`). The brief sketches `client.leaflet_SubmitFeedback(...)` — using the auto-generated NSwag client throws on non-2xx and loses the 409 semantic. Override the brief here.

6. **`KnowledgeBaseSearchAskTab.tsx` parity**: the existing inline `FeedbackForm` (`KnowledgeBaseSearchAskTab.tsx:93–163`) keeps its own `feedbackState` state machine. After extraction, the KB tab must own the equivalent state and pass `isSuccess`/`alreadySubmitted` props. Acceptance test: take a screenshot before extraction, run the flow after, expect identical visual + interaction (rating selection, success message, already-submitted message). Update the existing KB test (`useKnowledgeBase.test.ts` covers the mutation; add a `RagFeedbackForm.test.tsx` that exercises the prop contract).

7. **`LeafletResult` regenerate hides feedback form.** When `onRegenerate` is invoked, the parent (`LeafletGenerateTab`) starts a new generation; the parent should clear `generationId` immediately on regenerate-click so the stale form does not persist beside the loading skeleton. Add to FR-6 acceptance criteria: "Clicking Regenerate clears `generationId` until the new generation completes."

8. **Frontend i18n consumers.** Spec FR-7 adds translations to `frontend/src/i18n.ts` but does not say which UI surface consumes them. The `useSubmitLeafletFeedbackMutation` returns `{ alreadySubmitted: true }` (translated to a hardcoded message in `RagFeedbackForm`); the 404 path is unreachable from the UI (the form only shows when `generationId` is set, which the backend issued). Translations are still useful for future error-banner rendering if a generic error mapper exists; keep them, but do not add a render path that the UI does not exercise.

## Prerequisites

1. **Phase 1 merged into `feature/genai_consistency`.** Confirm `LeafletController` already has `Generate`, `useLeaflet.ts` exists, sidebar route exists. (Verified — see `LeafletController.cs:34`, `useLeaflet.ts`, `LeafletGenerateTab.tsx`.)

2. **Branch cut from `feature/genai_consistency`.** Per brief: integration branch is not `main`. Cut feature branch from it; PR targets it.

3. **Local dev DB up to date** with migration `20260505080157_AddSummaryToLeafletChunk`. The new migration `AddLeafletGenerations` will be sequential after this one and must apply cleanly via `dotnet ef database update`.

4. **`AuthorizationConstants.Policies.LeafletUpload` exists.** (Verified — `AuthenticationExtensions.cs:113`.) No new policy needed.

5. **`ICurrentUserService` registered** and returning a non-null `Id` for authenticated requests. (Verified — used in `QuestionLoggingBehavior` and `SubmitFeedbackHandler` already.) No new registration.

6. **OpenAPI client regeneration on backend build.** Adding endpoints + DTOs will regenerate `frontend/src/api/generated/api-client.ts` automatically. Verify the generated `LeafletFeedbackSummary` / `LeafletFeedbackStatsDto` shapes are usable; otherwise hand-roll the types in `useLeaflet.ts` (matches the existing pattern there which already hand-rolls types like `LeafletDocumentSummary`).

7. **No new infrastructure, secrets, or feature flags.** Pure code change against existing pgsql + EF Core + MediatR stack.
