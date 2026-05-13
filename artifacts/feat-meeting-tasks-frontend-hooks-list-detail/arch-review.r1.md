# Architecture Review: Meeting Tasks Frontend — Hooks, List, Detail, Config

## Skip Design: false

The feature introduces two new full pages (list + detail/validation), inline edit/add forms, status badges, a confirmation modal, and bulk actions. Visual hierarchy, status semantics (yellow/green/blue/red), Czech labels, and modal/focus behavior should be reviewed against the existing design system before implementation lands.

## Architectural Fit Assessment

The proposal **aligns with established frontend patterns** in this repo, with three deviations worth correcting before coding starts:

1. **Hook style** — The codebase has two coexisting hook styles:
   - **Generated NSwag client** (e.g. `useRecurringJobs.ts`) — preferred when the OpenAPI client has the endpoint typed.
   - **Raw fetch via `(apiClient as any).baseUrl` + `(apiClient as any).http.fetch`** (e.g. `useBackgroundRefresh.ts:11–29`, `usePurchaseOrders.ts:71–270`, `useAsyncInvoiceImport.ts`).
   The spec explicitly opts out of OpenAPI generation for this slice, so the raw-fetch pattern is correct here and matches `useBackgroundRefresh.ts` precedent.

2. **`getAuthenticatedApiClient()` is synchronous, not async** (see `frontend/src/api/client.ts:228–335`). The brief's `const apiClient = await getAuthenticatedApiClient()` is harmless (await on a non-promise) but misleading. Drop the `await` or leave it (existing `useBackgroundRefresh.ts` keeps it for consistency — match whichever the team prefers, but be aware it is *not* a promise).

3. **Page directory** — Spec says `frontend/src/pages/automation/`, but the sibling automation pages (`BackgroundTasks.tsx`, `InvoiceImportStatistics.tsx`) live at `frontend/src/components/pages/automation/`. The directory `src/pages/automation/` does not currently exist. Place the new pages alongside the existing automation pages for code locality and grep-ability.

The backend controller (`MeetingTasksController.cs`) is `[Authorize]` (Entra ID). The `getAuthenticatedApiClient()` Bearer-token flow is the right transport — no API key is needed from the frontend.

## Proposed Architecture

### Component Overview

```
frontend/src/
├── api/hooks/
│   └── useMeetingTasks.ts              ← React Query hooks + raw-fetch client
│                                         (DTOs, MEETING_TASKS_KEYS, hooks)
└── components/pages/automation/
    ├── MeetingTasksPage.tsx            ← list view (filters, pagination, table)
    ├── MeetingTaskDetailPage.tsx       ← detail/validation (edit, approve, submit)
    └── meeting-tasks/                  ← (optional) co-located sub-components
        ├── StatusBadge.tsx
        ├── TaskCard.tsx
        ├── AddTaskForm.tsx
        └── ConfirmSubmitModal.tsx

frontend/src/
├── App.tsx                              ← +2 routes
└── components/Layout/Sidebar.tsx        ← +1 nav item under "automatizace"

backend/src/Anela.Heblo.API/
└── appsettings.json                     ← +MeetingTasks section (TodoListName only —
                                          see Specification Amendments)
```

**Data flow (read):** `MeetingTasksPage` → `useMeetingTasksList(filter, page)` → `getAuthenticatedApiClient()` → `(client.http.fetch)` → `GET /api/meeting-tasks` → MediatR `GetTranscriptListHandler` → JSON envelope back through React Query cache.

**Data flow (mutate):** click approve/reject/edit/add/submit → mutation hook → PUT/POST endpoint → `onSuccess` invalidates `MEETING_TASKS_KEYS.detail(id)` (and `MEETING_TASKS_KEYS.list` for status/submit) → list/detail auto-refetch.

### Key Design Decisions

#### Decision 1: Hand-written DTOs vs. generated client
**Options considered:**
- A. Add endpoints to NSwag generation and use the typed client (`useRecurringJobs.ts` style).
- B. Hand-write DTOs + raw-fetch client in `useMeetingTasks.ts` (`useBackgroundRefresh.ts` style).

**Chosen approach:** B (hand-written, matching the spec).

**Rationale:** Spec explicitly excludes OpenAPI client generation for this slice. The raw-fetch pattern is already widespread in the repo (`useBackgroundRefresh`, `usePurchaseOrders`, `useAsyncInvoiceImport`, `useCatalog`, ~10 files). The team has accepted the trade-off of duplicating contracts in exchange for not blocking on backend client regeneration. **Mitigation:** add a `// TODO: migrate to generated client when /api/meeting-tasks is added to NSwag` comment so the debt is visible.

#### Decision 2: Status as string-literal union vs. `string`
**Options considered:**
- A. `status: string` (brief's signature).
- B. `status: "Pending" | "Approved" | "Rejected"` (spec's signature).

**Chosen approach:** B.

**Rationale:** Stronger compile-time guarantees, matches the global TS rule "prefer string-literal unions over enums". The handful of `Record<string, string>` map lookups in `StatusBadge` already handle unknown values defensively.

#### Decision 3: Bulk-approve sequential `await` vs. batched
**Options considered:**
- A. Spec's loop: `for (const t of pending) await updateStatus.mutateAsync(...)` (N round trips, N cache invalidations).
- B. New backend bulk endpoint.
- C. Single `Promise.all` + one trailing invalidation.

**Chosen approach:** A (no change from spec).

**Rationale:** N is small (tasks per meeting); spec rejects `Promise.all` to avoid optimistic-cache races. Worth a tiny improvement: have each `useUpdateProposedTaskStatus.onSuccess` skip detail-list invalidation when called from the bulk path, OR invalidate once at the end of the loop — but this is a follow-up, not a blocker.

#### Decision 4: New page directory vs. existing
**Chosen approach:** Place pages under `frontend/src/components/pages/automation/` (where `BackgroundTasks.tsx` lives), not `frontend/src/pages/automation/`.

**Rationale:** That directory already exists, holds the analogous "Background Tasks" page, and is what `App.tsx` imports from for automation routes. Creating a parallel `src/pages/automation/` fragments code locality with no offsetting benefit.

## Implementation Guidance

### Directory / Module Structure

- **Create:**
  - `frontend/src/api/hooks/useMeetingTasks.ts`
  - `frontend/src/components/pages/automation/MeetingTasksPage.tsx`
  - `frontend/src/components/pages/automation/MeetingTaskDetailPage.tsx`
- **Modify:**
  - `frontend/src/App.tsx` — add routes adjacent to `/automation/background-tasks` (line 387). Imports must match new path (`./components/pages/automation/MeetingTasksPage`).
  - `frontend/src/components/Layout/Sidebar.tsx` — insert nav item into the `automatizace` section (`Sidebar.tsx:278–310`) between `hangfire` and `data-quality`.
  - `backend/src/Anela.Heblo.API/appsettings.json` — see Specification Amendments.
- **Co-location is encouraged** if any of `MeetingTaskDetailPage`'s sub-blocks (StatusBadge, TaskCard, AddTaskForm, ConfirmSubmitModal) grow past ~80 lines: split into `components/pages/automation/meeting-tasks/`. The single-file page in the spec is acceptable while it stays under the project's 800-line file budget.

### Interfaces and Contracts

```typescript
// All exported from useMeetingTasks.ts
export type ProposedTaskStatus = 'Pending' | 'Approved' | 'Rejected';
export type TranscriptStatus = 'PendingReview' | 'Approved' | 'PartiallyApproved';

export interface ProposedTaskDto { /* per spec */ status: ProposedTaskStatus; … }
export interface MeetingTranscriptDto { /* per spec */ status: TranscriptStatus; … }

export interface TranscriptListResponse { success: boolean; items: MeetingTranscriptDto[]; totalCount: number; pageNumber: number; pageSize: number; totalPages: number; }
export interface TranscriptDetailResponse { success: boolean; transcript: MeetingTranscriptDto; }
export interface SubmitToTodoResponse { success: boolean; successCount: number; failedCount: number; errors: string[]; }
export interface AddProposedTaskResponse { success: boolean; task: ProposedTaskDto; }

export interface TaskFormData { title: string; description: string; assignee: string; dueDate: string | null; }

export const MEETING_TASKS_KEYS = {
  all: ['meetingTasks'] as const,
  list: ['meetingTasks'] as const,                 // shared prefix for list invalidation
  detail: (id: string) => ['meetingTasks', id] as const,
} as const;
```

**Mutation contract for invalidation:**
- `useUpdateProposedTask` / `useAddProposedTask` → invalidate `detail(transcriptId)`.
- `useUpdateProposedTaskStatus` / `useSubmitToTodo` → invalidate `detail(transcriptId)` AND `MEETING_TASKS_KEYS.list` (status changes can move the transcript across the filter buckets).

**Error contract:** Non-2xx throws `new Error('API error: ${response.status}')`. Toast notifications are emitted globally by `getAuthenticatedApiClient`'s `extractErrorMessage` path (`client.ts:172–225`). Components only consume `isError` / `isPending`.

### Data Flow

**List page render:**
1. `useState<string|undefined>(undefined)` filter, `useState<number>(1)` page.
2. `useMeetingTasksList(filter, page, 20)` → React Query keyed on `[...MEETING_TASKS_KEYS.list, filter, page, 20]`.
3. Row click → `navigate(/automation/meeting-tasks/${transcript.id})`.
4. Filter change → reset page to 1 (must do both `setStatusFilter` and `setPage(1)` in the same handler).

**Detail page validation flow:**
1. `useMeetingTaskDetail(id)` loads transcript+tasks.
2. User edits inline → `useUpdateProposedTask.mutateAsync({...})` → on success, query refetches; `editingTaskId` reset by `saveEdit` itself.
3. User clicks approve/reject → `useUpdateProposedTaskStatus.mutateAsync({...})`.
4. User clicks "Schvalit vse" → sequential awaits (spec NFR-1).
5. User clicks "Pridat ulohu" → reveals inline form, on submit `useAddProposedTask.mutateAsync` then resets form state.
6. User clicks "Odeslat do TODO" → opens modal → confirm → `useSubmitToTodo.mutateAsync(id)` → close modal on success; modal stays open with `text-red-600` error on `isError`.

**Empty-string `dueDate` handling:** In `saveEdit` and `handleAddTask`, send `dueDate || null` (spec FR-3 acceptance). Backend handler must accept `null`. (Already true — `UpdateProposedTaskRequest`/`AddProposedTaskRequest` are nullable; verify if not.)

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `MeetingTasks:ApiKey` config is unwired — `MeetingTasksOptions.cs` only declares `TodoListName`. Adding `ApiKey` as committed config without a consumer creates dead config and confuses future readers. | **High** | Either (a) defer the `ApiKey` line to the subtask that introduces the Plaud ingest endpoint, OR (b) add an `ApiKey` property to `MeetingTasksOptions` in this PR even if unused (document the intent). Recommend (a). |
| Spec/brief place pages at `frontend/src/pages/automation/`, which doesn't exist; existing automation pages live at `frontend/src/components/pages/automation/`. Creating a parallel folder splits the automation feature. | **High** | Place pages at `frontend/src/components/pages/automation/`. Update `App.tsx` imports accordingly. |
| Brief's example uses `await getAuthenticatedApiClient()` but the function is synchronous. Copy-pasting this confuses readers about whether token acquisition awaits here. | **Low** | Match `useBackgroundRefresh.ts` exactly. Either keep the misleading `await` for consistency with that file, or drop it everywhere — pick one and document. |
| Sequential bulk-approve fires N detail-list invalidations (one per task), causing redundant refetches. | **Low** | Acceptable for N<20 per spec NFR-1. Possible follow-up: pass a `silent` flag to skip invalidation, or `invalidateQueries` once after the loop. |
| Centralized error toast from `getAuthenticatedApiClient` fires *and* React Query exposes `isError`. The detail-page submit modal shows red error text on top of a global toast. | **Medium** | Pass `showErrorToasts: false` to `getAuthenticatedApiClient(false)` from the submit path if double-notification is unwanted, OR rely on the spec's existing behavior (toast = general, inline = modal-specific). Decide explicitly. |
| `as any` casts on `apiClient.baseUrl` / `apiClient.http` are fragile against NSwag regeneration. | **Low** | Existing pattern across ~10 hooks; team has accepted this. Add a one-line comment in `useMeetingTasks.ts` referencing the pattern. |
| Spec asserts "no focus trap" for the modal — acceptable but not ideal for accessibility. | **Low** | Matches existing patterns; accept. Consider a follow-up issue for repo-wide modal a11y. |
| Static map lookups (`colorMap[status]`) silently fall through on a backend rename of `PendingReview`. | **Low** | Spec's fallback to gray + raw status string is sufficient. Optionally, narrow with a `satisfies Record<TranscriptStatus, string>` check. |

## Specification Amendments

1. **FR-5: split into separate subtask or constrain to existing options.** `MeetingTasksOptions` (`backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksOptions.cs`) currently has only `TodoListName`. Two options:
   - **Preferred:** Drop the `ApiKey` field from this PR. Add it to `appsettings.json` only when the subtask that introduces a Plaud ingest controller (API-key-auth) lands — that subtask should also extend `MeetingTasksOptions` with `ApiKey`.
   - **Alternative:** If you want to commit `ApiKey` now, also add `public string ApiKey { get; set; } = "";` to `MeetingTasksOptions` so it's bound and the config is not dead.

2. **FR-2 / FR-3 directory path:** change `frontend/src/pages/automation/` → `frontend/src/components/pages/automation/` throughout the spec and update the `App.tsx` import paths.

3. **DTO `status` type:** Brief uses `string`; spec already uses string-literal unions. Lock in spec's tighter typing in the implementation (`ProposedTaskStatus`, `TranscriptStatus`).

4. **Reusable `TaskFormData` type:** The same shape (`title`, `description`, `assignee`, `dueDate`) is used by edit and add flows. Export a single `TaskFormData` interface and reuse it (`useState<TaskFormData>`, `updateTask`/`addTask` payloads).

5. **Invalidation contract for `useUpdateProposedTaskStatus`:** Spec correctly invalidates both detail and list. Make sure the brief code matches (it does, see `useUpdateProposedTaskStatus` in the brief).

6. **Sidebar nav position:** Confirm spec's placement "after hangfire" (= between `hangfire` and `data-quality` in `Sidebar.tsx:299–308`). Acceptable; no change needed.

## Prerequisites

- **Backend endpoints (subtasks 1–9) deployed** at `/api/meeting-tasks/*` with `[Authorize]` — already in place per `MeetingTasksController.cs`.
- **Database migration `AddMeetingTasksTables`** applied — exists at `Persistence/Migrations/20260512191541_AddMeetingTasksTables.cs`. Run manually per project facts in CLAUDE.md.
- **Microsoft Graph TODO integration** required only for live submit; UI compiles and renders without it.
- **No frontend NSwag regeneration needed** — endpoints intentionally not added to the generated client for this slice.
- **No new npm dependencies** — `@tanstack/react-query`, `react-router-dom`, `lucide-react` already in `package.json`.
- **Czech label sanity check** with the product owner before final commit (no diacritics per spec NFR-3; verify "Vse", "Castecne", "Rucne pridano" etc. read correctly to a Czech reader).