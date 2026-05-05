# Specification: Leaflet Generation Persistence + Feedback (Phase 2)

## Summary
Persist every AI-generated leaflet with its source counts and metadata so users can submit a one-shot 1–5 precision/style rating with optional comment. Mirrors the proven KnowledgeBase feedback pattern (owner-only, single submission) and exposes a manager-facing feedback list endpoint with paging, filtering, and aggregate statistics.

## Background
Phase 1 delivered the leaflet generator UI (sidebar, routes, generation flow). Phase 2 closes the feedback loop: generated content currently disappears once rendered, leaving no way to track quality, gather user ratings, or iterate on prompt/RAG performance. The KnowledgeBase module already implements an owner-only feedback workflow with rating + comment + admin list; Phase 2 reuses that pattern verbatim for leaflets, including extracting the rating form into a shared `RagFeedbackForm` component to avoid duplication. This phase is a prerequisite for any analytical or quality-improvement work in Phase 3+ but is otherwise independent of those phases.

## Functional Requirements

### FR-1: Persist every successful leaflet generation
A new MediatR pipeline behavior (`LeafletGenerationLoggingBehavior`) intercepts every `GenerateLeafletRequest`/`GenerateLeafletResponse` round-trip and, on success, persists a `LeafletGeneration` row capturing the request inputs, final markdown, RAG source counts, duration, and the current user's id. The behavior must run after the handler completes so it can include actual generation duration and outputs.

**Acceptance criteria:**
- Successful generations write exactly one row to `LeafletGenerations` with: `Topic`, `Audience` (enum name), `Length` (enum name), `FinalMarkdown`, `KbSourceCount`, `LeafletSourceCount`, `DurationMs`, `CreatedAt` (UTC), `UserId`.
- The newly assigned `Guid Id` is returned to the client via `GenerateLeafletResponse.Id`.
- Failed generations (`response.Success == false`) do **not** write any row.
- If persistence throws, the exception is logged via `ILogger` and swallowed — the generation response still returns to the caller successfully (with `Id` left null).
- `GenerateLeafletResponse` exposes `KbSourceCount` and `LeafletSourceCount` populated by `GenerateLeafletHandler` from its existing `kbHits`/`leafletHits` lists.

### FR-2: One-shot, owner-only feedback submission
Authenticated users can submit precision (1–5), style (1–5), and an optional ≤1000-character comment for a generation **they own**. Submission is permitted exactly once per generation.

**Acceptance criteria:**
- `POST /api/leaflet/feedback` with `{ generationId, precisionScore, styleScore, comment? }` validates ranges via data annotations.
- Returns `LeafletFeedbackNotFound` when the `generationId` does not exist.
- Returns `Forbidden` when the current user's id does not match `generation.UserId`.
- Returns `LeafletFeedbackAlreadySubmitted` when either `PrecisionScore` or `StyleScore` is already set.
- On success, persists `PrecisionScore`, `StyleScore`, `FeedbackComment` to the existing row and returns `200 OK`.
- Two new error codes added to `ErrorCodes`: `LeafletFeedbackNotFound`, `LeafletFeedbackAlreadySubmitted`.

### FR-3: Manager feedback list endpoint
Users with the `LeafletUpload` policy can list all leaflet generations with their feedback, filtered and paged.

**Acceptance criteria:**
- `GET /api/leaflet/feedback/list` requires `[Authorize(Policy = AuthorizationConstants.Policies.LeafletUpload)]`.
- Query parameters: `hasFeedback?` (bool), `userId?`, `sortBy` (default `CreatedAt`), `sortDescending` (default `true`), `pageNumber` (default 1), `pageSize` (default 20).
- Allowed `sortBy` values: `CreatedAt`, `PrecisionScore`, `StyleScore`. Invalid values fall back to `CreatedAt`.
- Allowed `pageSize` values: `10`, `20`, `50`. Invalid values fall back to 20.
- Response includes `Logs` (full row data including markdown), `TotalCount`, `PageNumber`, `PageSize`, and `Stats { TotalGenerations, TotalWithFeedback, AvgPrecisionScore?, AvgStyleScore? }`.
- `hasFeedback=true` returns rows with at least one of `PrecisionScore`/`StyleScore` set; `hasFeedback=false` returns rows where both are null; omitting the param returns all rows.

### FR-4: Single-generation lookup endpoint
A trivial loader endpoint allows the frontend (and future tooling) to refetch a single generation by id.

**Acceptance criteria:**
- `GET /api/leaflet/generations/{id:guid}` returns the row mapped to a `GetLeafletGenerationResponse` DTO.
- Returns 404 (via `BaseResponse` error code) when the id is not found.
- Implementation pattern mirrors `GetArticleHandler`.

### FR-5: Shared `RagFeedbackForm` component
The KnowledgeBase feedback form (currently inline in `KnowledgeBaseSearchAskTab.tsx`, lines ~93–163) is extracted into `frontend/src/components/feedback/RagFeedbackForm.tsx` with a hook-agnostic prop contract so both KB and leaflet flows can use it.

**Acceptance criteria:**
- New component file accepts: `onSubmit({ precisionScore, styleScore, comment })`, `isSubmitting`, `alreadySubmitted`, `isSuccess`.
- `KnowledgeBaseSearchAskTab.tsx` imports and uses the new component; behavior identical to current implementation (no visual or functional regressions in KB flow).
- Component renders ratings UI, comment textarea (max 1000 chars), submit button, and the "already submitted" / success states from the KB original.

### FR-6: Leaflet result feedback UI
After a leaflet is generated, the result view shows the new feedback form bound to the generation id returned by the API.

**Acceptance criteria:**
- `LeafletResult.tsx` accepts an optional `generationId: string` prop.
- When `generationId` is present, `RagFeedbackForm` is rendered below the existing Copy/Regenerate buttons.
- When `generationId` is null/undefined (logging-behavior failure), the form is hidden.
- Submission uses a new `useSubmitLeafletFeedbackMutation` hook that maps HTTP 409 to an `alreadySubmitted` state, mirroring the KB hook.
- A `useLeafletFeedbackListQuery(params)` hook is added with a 30s `staleTime` for the manager list view.

### FR-7: Localization
Czech and English translations exist for the two new error codes.

**Acceptance criteria:**
- `LeafletFeedbackNotFound`: cs = "Záznam generování letáku nebyl nalezen.", en = "Leaflet generation log not found."
- `LeafletFeedbackAlreadySubmitted`: cs = "Zpětná vazba již byla odeslána.", en = "Feedback has already been submitted."

### FR-8: Integration branch and PR target
All Phase 2 work lives on a branch based on `feature/genai_consistency` and is opened as a PR back to `feature/genai_consistency` — **not** `main`.

**Acceptance criteria:**
- New feature branch is created from `origin/feature/genai_consistency`.
- The PR's base branch is `feature/genai_consistency`.

## Non-Functional Requirements

### NFR-1: Performance
- Logging behavior overhead per generation: under 50 ms in the happy path (single insert with no related entities).
- Feedback list endpoint: under 500 ms server-side for typical page sizes (≤50 rows) on a dataset of ≤100k generations, supported by indexes on `CreatedAt`, `UserId`, and a filtered index on `PrecisionScore IS NOT NULL`.
- Mutation hook surfaces 409 → `alreadySubmitted` without an additional network round trip.

### NFR-2: Security & authorization
- `POST /api/leaflet/feedback` requires authentication; the handler enforces owner-only by comparing `generation.UserId` to `ICurrentUserService.GetCurrentUser().Id`. Non-owners receive `Forbidden`.
- `GET /api/leaflet/feedback/list` requires the `LeafletUpload` policy (managerial role).
- `GET /api/leaflet/generations/{id}` requires authentication; further authorization is consistent with how other Leaflet read endpoints are protected (no policy beyond authentication unless existing convention dictates otherwise).
- `FinalMarkdown` and `FeedbackComment` may contain user content but are stored verbatim; no additional sanitization is applied at write time. Rendering follows existing markdown-render rules in the frontend.
- Logging behavior swallows persistence errors silently from the user's perspective but always logs them with topic context for diagnostics.

### NFR-3: Reliability
- Generation must succeed even if logging fails. Failure modes: DB connection loss, schema drift, transient errors — all caught and logged in the behavior's `try/catch`.
- Migration `AddLeafletGenerations` applies cleanly to existing dev/staging databases without data loss; rollback is supported via `dotnet ef migrations remove` until applied to production.

### NFR-4: Observability
- All logging-behavior failures emit an `ILogger` error entry with the originating `Topic` so generation issues can be correlated.
- The list endpoint's stats payload (`TotalGenerations`, `TotalWithFeedback`, averages) supports lightweight quality monitoring without additional infrastructure.

### NFR-5: Test coverage (≥80% for new code paths)
Backend:
- `SubmitLeafletFeedbackHandlerTests`: not-found, forbidden, already-submitted, success.
- `GetLeafletFeedbackListHandlerTests`: paging, filters (`hasFeedback`, `userId`), sort columns, stats accuracy.
- `LeafletGenerationLoggingBehaviorTests`: row saved on success, `response.Id` populated, exceptions swallowed and logged, no save on `response.Success == false`.
- `LeafletRepositoryIntegrationTests`: `SaveGenerationAsync`, `GetGenerationByIdAsync`, `GetGenerationsPagedAsync`, `GetGenerationStatsAsync`.

Frontend:
- `useLeaflet.test.ts`: `useSubmitLeafletFeedbackMutation` converts HTTP 409 → `alreadySubmitted` state.
- `RagFeedbackForm.test.tsx`: renders rating controls, submits with values, displays already-submitted state, displays success state.

## Data Model

### Entity: `LeafletGeneration`
Namespace: `Anela.Heblo.Domain.Features.Leaflet`. POCO (not a record — DTO-record collision risk does not apply here, but consistency with existing domain-class style).

| Field              | Type             | Notes                                            |
|--------------------|------------------|--------------------------------------------------|
| `Id`               | `Guid`           | PK, generated in behavior                        |
| `Topic`            | `string`         | max length 200, required                         |
| `Audience`         | `string`         | max length 50, required, `AudienceType` name     |
| `Length`           | `string`         | max length 50, required, `LeafletLength` name    |
| `FinalMarkdown`    | `string`         | required, no max length (text)                   |
| `KbSourceCount`    | `int`            | KB chunks used                                   |
| `LeafletSourceCount` | `int`          | leaflet chunks used                              |
| `DurationMs`       | `long`           | wall-clock generation duration                   |
| `CreatedAt`        | `DateTimeOffset` | UTC                                              |
| `UserId`           | `string?`        | max length 200, nullable for system-initiated    |
| `PrecisionScore`   | `int?`           | 1–5, set on feedback                             |
| `StyleScore`       | `int?`           | 1–5, set on feedback                             |
| `FeedbackComment`  | `string?`        | column type `text`, ≤1000 chars                  |

**Indexes:**
- `IX_LeafletGenerations_CreatedAt` on `CreatedAt`
- `IX_LeafletGenerations_UserId` on `UserId`
- `IX_LeafletGenerations_PrecisionScore` on `PrecisionScore` with filter `"PrecisionScore" IS NOT NULL`

**Table name:** `LeafletGenerations` (PostgreSQL public schema).

### Value object: `LeafletFeedbackStats`
`record LeafletFeedbackStats(int TotalGenerations, int TotalWithFeedback, double? AvgPrecisionScore, double? AvgStyleScore);` — domain-internal projection only, not exposed directly via API (mapped to `LeafletFeedbackStatsDto`).

### Migration
Name: `AddLeafletGenerations`, sequential after `20260505080157_AddSummaryToLeafletChunk`.
Command: `dotnet ef migrations add AddLeafletGenerations --project backend/src/Anela.Heblo.Persistence --startup-project backend/src/Anela.Heblo.API`.

## API / Interface Design

### Backend endpoints (all under `LeafletController`)

**`POST /api/leaflet/feedback`**
- Body: `SubmitLeafletFeedbackRequest { Guid GenerationId, [Range(1,5)] int PrecisionScore, [Range(1,5)] int StyleScore, [MaxLength(1000)] string? Comment }`
- 200: `SubmitLeafletFeedbackResponse` (success)
- 4xx: `BaseResponse` error envelope with one of `LeafletFeedbackNotFound`, `Forbidden`, `LeafletFeedbackAlreadySubmitted`
- Maps to HTTP 409 for `LeafletFeedbackAlreadySubmitted` (matches KB convention).

**`GET /api/leaflet/feedback/list`** *(policy: `LeafletUpload`)*
- Query: `hasFeedback?`, `userId?`, `sortBy=CreatedAt`, `sortDescending=true`, `pageNumber=1`, `pageSize=20`
- 200: `GetLeafletFeedbackListResponse { List<LeafletFeedbackSummary> Logs, int TotalCount, int PageNumber, int PageSize, LeafletFeedbackStatsDto Stats }`

**`GET /api/leaflet/generations/{id:guid}`**
- 200: `GetLeafletGenerationResponse` with the full row
- 404 via error envelope when not found

### Updated existing surface
- `GenerateLeafletResponse`: adds `Guid? Id`, plus `int KbSourceCount` and `int LeafletSourceCount` if not already present.

### Repository (`ILeafletRepository`) — new methods
```csharp
Task SaveGenerationAsync(LeafletGeneration generation, CancellationToken ct);
Task<LeafletGeneration?> GetGenerationByIdAsync(Guid id, CancellationToken ct);
Task<(IReadOnlyList<LeafletGeneration> Items, int TotalCount)> GetGenerationsPagedAsync(
    bool? hasFeedback, string? userId, string sortBy, bool descending,
    int page, int pageSize, CancellationToken ct);
Task<LeafletFeedbackStats> GetGenerationStatsAsync(CancellationToken ct);
Task SaveChangesAsync(CancellationToken ct);
```

### MediatR pipeline behavior
`LeafletGenerationLoggingBehavior : IPipelineBehavior<GenerateLeafletRequest, GenerateLeafletResponse>` — registered scoped in `LeafletModule.cs` after existing service registrations.

### Frontend surface
- New component: `frontend/src/components/feedback/RagFeedbackForm.tsx`.
- New hooks in `frontend/src/api/hooks/useLeaflet.ts`: `useSubmitLeafletFeedbackMutation`, `useLeafletFeedbackListQuery`.
- Updated `LeafletResult.tsx` props: `{ content: string; generationId?: string; onRegenerate: () => void }`.
- Updated `KnowledgeBaseSearchAskTab.tsx`: replace inline form with `<RagFeedbackForm />`.
- New i18n entries in `frontend/src/i18n.ts`.

## Dependencies

**Internal:**
- Phase 1 (sidebar, routes, leaflet generation handler, `GenerateLeafletRequest`/`Response`) must be merged into `feature/genai_consistency`.
- `ICurrentUserService` — already present, used identically to KB.
- `ILeafletRepository` — extended in this phase.
- `BaseResponse`, `ErrorCodes`, `AuthorizationConstants.Policies.LeafletUpload` — existing infrastructure.
- Existing KB `SubmitFeedbackHandler`, `GetFeedbackListHandler`, `FeedbackLogSummary`, `FeedbackStatsDto` — used as reference templates.
- Existing `QuestionLoggingBehavior` — used as the structural template for `LeafletGenerationLoggingBehavior`.

**External:**
- MediatR (existing).
- Entity Framework Core 8 + PostgreSQL provider (existing).
- React Query (existing) for the new frontend hooks.
- No new packages required.

**Branch dependency:** All work is based on and merged into `feature/genai_consistency`.

## Out of Scope
- A separate `LeafletGenerationSource` table linking generations to specific KB/leaflet chunk ids — counts are sufficient for Phase 2; per-chunk traceability is deferred to Phase 4+.
- Edit/update of submitted feedback — submission is one-shot by design.
- Anonymized analytics dashboards or aggregate exports — only the list endpoint + stats payload is provided.
- Rate limiting on feedback submission beyond the inherent one-shot constraint.
- Migration auto-application — per project convention, migrations remain manual.
- E2E Playwright tests for the new feedback flow (project E2E suite runs nightly and is not part of PR CI; new E2E tests can be added incrementally).
- Changes to the KB feedback backend — only the frontend form is extracted into a shared component.
- Admin-side moderation tooling for `FeedbackComment` content.

## Open Questions
None.

## Status: COMPLETE