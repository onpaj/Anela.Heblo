# Specification: Leaflet Generation Persistence + Feedback (Phase 2)

## Summary
Persist every generated leaflet (topic, parameters, final markdown, source counts, duration, user) to the database and expose a one-shot, owner-only feedback flow (1–5 precision + style ratings, optional comment) mirroring the existing KnowledgeBase feedback pattern. A manager-gated list endpoint surfaces all generations with aggregated stats.

## Background
Phase 1 of the Leaflet Generator (sidebar, routes, generation flow) is complete. Generated leaflets currently disappear after rendering — there is no audit trail, no quality signal, and no way to compare runs or improve the underlying RAG pipeline.

KnowledgeBase Q&A already has a proven persistence + feedback pattern (`QuestionLoggingBehavior`, `SubmitFeedbackHandler`, `GetFeedbackListHandler`). Phase 2 ports that pattern to the Leaflet feature so we can:
- retain generated content per user for later reference,
- collect explicit quality signals (precision and style),
- aggregate stats to drive future RAG tuning (Phase 3/4).

Phase 2 is independent from Phase 3/4 and depends only on Phase 1 being merged.

**Branch policy:** integration branch is `feature/genai_consistency` (not `main`). Feature branch must be cut from it, and the PR must target it.

## Functional Requirements

### FR-1: Persist every successful leaflet generation
After a successful `GenerateLeafletRequest`, a `LeafletGeneration` row is written via a MediatR `IPipelineBehavior` (`LeafletGenerationLoggingBehavior`), capturing topic, audience, length, final markdown, KB and Leaflet source counts, duration, current user id, and creation timestamp. The behavior must mirror `QuestionLoggingBehavior` for KB.

**Acceptance criteria:**
- A successful generation produces exactly one row in `LeafletGenerations` with all fields populated.
- A failed generation (`response.Success == false`) writes nothing.
- Logging failures are caught and logged; the original generation response is still returned to the caller.
- The newly created `Id` is written back to `GenerateLeafletResponse.Id` so the frontend can attach feedback.
- `GenerateLeafletResponse.KbSourceCount` and `LeafletSourceCount` are populated by `GenerateLeafletHandler` from the existing KB/leaflet hit lists.

### FR-2: One-shot, owner-only feedback submission
`POST /api/leaflet/feedback` accepts `{ generationId, precisionScore (1–5), styleScore (1–5), comment? (≤1000 chars) }` and updates the generation row.

**Acceptance criteria:**
- Returns success when the caller is the owner of the generation and no feedback was previously submitted.
- Returns `LeafletFeedbackNotFound` (HTTP 404 envelope) when the id is unknown.
- Returns `Forbidden` when the caller is not the generation owner (`UserId` mismatch).
- Returns `LeafletFeedbackAlreadySubmitted` (HTTP 409 envelope) when `PrecisionScore` or `StyleScore` is already set.
- `precisionScore` and `styleScore` validated `[Range(1,5)]`; `comment` validated `[MaxLength(1000)]`.
- On success, `PrecisionScore`, `StyleScore`, and `FeedbackComment` are persisted in a single `SaveChangesAsync`.

### FR-3: Manager-gated feedback list with stats
`GET /api/leaflet/feedback/list` returns paged generations with optional filters and aggregated stats. Endpoint is gated by the `LeafletUpload` policy (same policy used elsewhere in the leaflet feature for elevated access).

**Acceptance criteria:**
- Query parameters: `hasFeedback?`, `userId?`, `sortBy` (default `CreatedAt`), `sortDescending` (default `true`), `pageNumber` (default 1), `pageSize` (default 20).
- Allowed `sortBy` values: `CreatedAt`, `PrecisionScore`, `StyleScore`. Invalid values rejected.
- Allowed `pageSize` values: `10`, `20`, `50`. Other values rejected.
- Response includes: `Logs[]`, `TotalCount`, `PageNumber`, `PageSize`, and `Stats { TotalGenerations, TotalWithFeedback, AvgPrecisionScore?, AvgStyleScore? }`.
- `hasFeedback=true` filters to rows where `PrecisionScore != null OR StyleScore != null`; `hasFeedback=false` filters to neither set.

### FR-4: Single-generation lookup
`GET /api/leaflet/generations/{id}` loads a `LeafletGeneration` by id. Returns 404 envelope when not found. Pattern mirrors `GetArticleHandler`.

**Acceptance criteria:**
- Returns the full generation payload for any caller authenticated by the existing controller-level policy.
- Returns `LeafletFeedbackNotFound` (or equivalent not-found code consistent with existing leaflet handlers) when missing.

### FR-5: Reusable feedback form component
Extract the inline feedback form currently in `KnowledgeBaseSearchAskTab.tsx` (≈ lines 93–163) into `frontend/src/components/feedback/RagFeedbackForm.tsx` with a generic prop contract: `{ onSubmit, isSubmitting, alreadySubmitted, isSuccess }`. Update KB tab to use the shared component.

**Acceptance criteria:**
- KB feedback flow behaves identically before and after extraction (visual + behavior parity).
- New component has no KB- or leaflet-specific imports.
- Tests cover: rating selection, comment input, submit invocation, already-submitted state, success state.

### FR-6: Leaflet result UI shows feedback form
`LeafletResult.tsx` accepts an optional `generationId`. When set, the component renders `RagFeedbackForm` below the Copy/Regenerate buttons and wires it to `useSubmitLeafletFeedbackMutation`.

**Acceptance criteria:**
- Form is hidden when `generationId` is absent (e.g., logging failed).
- HTTP 409 from the backend is surfaced as `alreadySubmitted = true`.
- Successful submission disables the form and shows the success indicator.

### FR-7: i18n entries for new error codes
Add Czech and English translations for `LeafletFeedbackNotFound` and `LeafletFeedbackAlreadySubmitted` in `frontend/src/i18n.ts`.

**Acceptance criteria:**
- Czech: "Záznam generování letáku nebyl nalezen." / "Zpětná vazba již byla odeslána."
- English: "Leaflet generation log not found." / "Feedback has already been submitted."

## Non-Functional Requirements

### NFR-1: Performance
- Logging behavior must not measurably slow successful generations. Generation latency dominates (LLM call); the DB insert is one row in a try/catch and runs after `next()` returns.
- `GetGenerationsPagedAsync` must use server-side paging and indexed sorts. Indexes on `CreatedAt`, `UserId`, and a filtered index on `PrecisionScore` (where not null) are required.
- Stats query is acceptable as four separate aggregates against a single table; no caching required at MVP scale.

### NFR-2: Security
- Feedback submission is owner-only — enforced by handler comparing `generation.UserId` against `ICurrentUserService.GetCurrentUser().Id`. Do not rely on client-side checks.
- Feedback list endpoint is gated by `AuthorizationConstants.Policies.LeafletUpload`.
- Single-generation GET inherits the controller's authentication. (See Open Questions on whether owner-only restriction also applies here.)
- `FeedbackComment` is stored as `text`; render as plain text on the frontend (no HTML interpretation) to avoid XSS.
- Validation attributes (`Range`, `MaxLength`) enforced at the request DTO boundary.

### NFR-3: Reliability
- Logging failures are swallowed and logged via `ILogger` — generation never fails because of a persistence problem.
- Migration must apply cleanly on the existing dev database without manual SQL.
- Feedback write is a single `SaveChangesAsync` on a tracked entity (no concurrency token at MVP — last-write-wins is fine because the one-shot guard prevents the second write).

### NFR-4: Maintainability
- The KB pattern is the source of truth — leaflet handlers, behavior, DTOs, and frontend hooks must structurally mirror their KB counterparts so future changes can be applied to both with minimal divergence.
- DTOs are classes (not records) per project convention.

## Data Model

### `LeafletGeneration` (domain entity, persisted)
| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | PK |
| `Topic` | `string` | required, max 200 |
| `Audience` | `string` | required, max 50 — `AudienceType` enum name |
| `Length` | `string` | required, max 50 — `LeafletLength` enum name |
| `FinalMarkdown` | `string` | required, unbounded |
| `KbSourceCount` | `int` | |
| `LeafletSourceCount` | `int` | |
| `DurationMs` | `long` | total handler time |
| `CreatedAt` | `DateTimeOffset` | UTC |
| `UserId` | `string?` | max 200 — null if generation was anonymous (shouldn't occur in practice) |
| `PrecisionScore` | `int?` | 1–5, null until feedback submitted |
| `StyleScore` | `int?` | 1–5, null until feedback submitted |
| `FeedbackComment` | `string?` | `text` column, ≤1000 chars enforced at API layer |

**Indexes:**
- `CreatedAt`
- `UserId`
- `PrecisionScore` filtered on `IS NOT NULL`

**Table name:** `public."LeafletGenerations"`.

### `LeafletFeedbackStats` (record, in-memory only)
`(int TotalGenerations, int TotalWithFeedback, double? AvgPrecisionScore, double? AvgStyleScore)`.

### Out-of-scope storage
No separate `LeafletGenerationSource` table — only counts are stored. Per-chunk traceability is deferred to Phase 4+.

## API / Interface Design

### Backend endpoints (controller: `LeafletController`)

| Method | Path | Auth | Body / Query | Response |
|---|---|---|---|---|
| `POST` | `/api/leaflet/feedback` | authenticated, owner-only enforced in handler | `SubmitLeafletFeedbackRequest` | `SubmitLeafletFeedbackResponse` (200) / 404 / 409 envelopes |
| `GET` | `/api/leaflet/feedback/list` | `LeafletUpload` policy | `hasFeedback?`, `userId?`, `sortBy`, `sortDescending`, `pageNumber`, `pageSize` | `GetLeafletFeedbackListResponse` |
| `GET` | `/api/leaflet/generations/{id:guid}` | controller default | path id | `GetLeafletGenerationResponse` |

The existing `POST /api/leaflet/generate` endpoint continues to return `GenerateLeafletResponse`, now with a populated `Id: Guid?` field.

### MediatR pipeline
Register `LeafletGenerationLoggingBehavior` as `IPipelineBehavior<GenerateLeafletRequest, GenerateLeafletResponse>` in `LeafletModule`.

### Repository (`ILeafletRepository`)
New methods:
- `SaveGenerationAsync(LeafletGeneration, CancellationToken)`
- `GetGenerationByIdAsync(Guid, CancellationToken)`
- `GetGenerationsPagedAsync(bool? hasFeedback, string? userId, string sortBy, bool descending, int page, int pageSize, CancellationToken)`
- `GetGenerationStatsAsync(CancellationToken)`
- `SaveChangesAsync(CancellationToken)`

### Error codes (added to `ErrorCodes`)
- `LeafletFeedbackNotFound`
- `LeafletFeedbackAlreadySubmitted`

(`Forbidden` already exists.)

### Frontend
- Component: `frontend/src/components/feedback/RagFeedbackForm.tsx` (generic, prop-driven).
- Hooks (added to `frontend/src/api/hooks/useLeaflet.ts`):
  - `useSubmitLeafletFeedbackMutation()` — converts HTTP 409 → `alreadySubmitted`.
  - `useLeafletFeedbackListQuery(params)` — `staleTime: 30_000`.
- Updated component: `LeafletResult.tsx` accepts `generationId?` and renders the form when present.
- Updated component: `KnowledgeBaseSearchAskTab.tsx` consumes `RagFeedbackForm` instead of its inline form.
- i18n: two new error code translations (cs/en).

## Dependencies
- **Phase 1** (sidebar, routes, generation handler) must be merged into `feature/genai_consistency` before Phase 2 starts.
- `feature/genai_consistency` integration branch — feature branch is cut from it, PR targets it.
- Existing infrastructure: MediatR, EF Core, `ICurrentUserService`, `BaseResponse`, `ErrorCodes`, `AuthorizationConstants.Policies.LeafletUpload`.
- Existing OpenAPI client generation pipeline (TypeScript client regenerated on backend build).
- Reference patterns: `QuestionLoggingBehavior`, `SubmitFeedbackHandler`, `GetFeedbackListHandler`, `GetArticleHandler` (KB feature).

## Out of Scope
- Per-chunk source tracking (`LeafletGenerationSource` table, cross-navigation to KB chunks).
- Editing or deleting submitted feedback.
- Editing or deleting generations.
- Anonymous generations (`UserId == null` is allowed in the schema but not a primary use case).
- Caching the stats query.
- Concurrency tokens on `LeafletGeneration`.
- Aggregated dashboards / analytics views (consumed via list endpoint only).
- Phase 3 / Phase 4 enhancements (regeneration with feedback context, prompt tuning loops, etc.).
- Database migration automation — migrations remain manual per project convention.
- E2E tests — phase 2 ships unit + integration coverage; E2E lives in nightly suite and can be added later.

## Open Questions
None.

## Status: COMPLETE