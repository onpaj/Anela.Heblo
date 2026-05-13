# Specification: Meeting Tasks Frontend — Hooks, List Page, Detail Page, Config

## Summary
Build the frontend layer for the Meeting Task Validation Checkpoint feature: React Query hooks wrapping the meeting-tasks API, a list page with filtering and pagination, a detail/validation page for editing/approving/rejecting proposed tasks and submitting them to Microsoft TODO, plus app-wide wiring (route, sidebar nav, backend config). The user reviews AI-extracted meeting tasks before they reach Microsoft TODO.

## Background
The parent epic (`feat/meeting-task-validation-epic`) introduces a validation checkpoint between AI-extracted meeting transcripts (from Plaud recordings) and the Microsoft TODO list. Backend tasks (1–9) already define the API surface (`/api/meeting-tasks`) and Graph integration. This subtask delivers the user-facing UI plus the configuration plumbing that makes the feature operable end-to-end. Czech UI strings are intentional — the application is Czech-localized.

## Functional Requirements

### FR-1: React Query Hooks
Create `frontend/src/api/hooks/useMeetingTasks.ts` exposing typed hooks and a thin API client wrapping `getAuthenticatedApiClient()` with absolute URLs (`${apiClient.baseUrl}${relativeUrl}`).

**Hooks:**
- `useMeetingTasksList(statusFilter?, page=1, pageSize=20)` — GET `/api/meeting-tasks?statusFilter=&pageNumber=&pageSize=`
- `useMeetingTaskDetail(id)` — GET `/api/meeting-tasks/{id}`, disabled when `id` falsy
- `useUpdateProposedTask()` — PUT `/api/meeting-tasks/{transcriptId}/tasks/{taskId}`
- `useUpdateProposedTaskStatus()` — PUT `/api/meeting-tasks/{transcriptId}/tasks/{taskId}/status`
- `useAddProposedTask()` — POST `/api/meeting-tasks/{transcriptId}/tasks`
- `useSubmitToTodo()` — POST `/api/meeting-tasks/{transcriptId}/submit`

**Acceptance criteria:**
- Mutation `onSuccess` invalidates `MEETING_TASKS_KEYS.detail(transcriptId)`; status changes and submit also invalidate `MEETING_TASKS_KEYS.list`.
- Query keys exported as `MEETING_TASKS_KEYS` constant.
- All DTO interfaces (`ProposedTaskDto`, `MeetingTranscriptDto`, list/detail/submit/add responses) exported.
- Non-2xx responses throw `Error("API error: {status}")`.
- File compiles under `npm run build` with strict TypeScript.

### FR-2: Meeting Tasks List Page
Create `frontend/src/pages/automation/MeetingTasksPage.tsx`.

**Layout:**
- Header: title "Meeting Tasks" + Czech subtitle.
- Filter buttons: "Vse" (no filter), "Ke kontrole" (`PendingReview`), "Schvaleno" (`Approved`). Active state highlights the selected filter.
- Table columns: Predmet (subject), Prijato (receivedAt, cs-CZ date), Ulohy (taskCount + optional "({approvedTaskCount} schvaleno)"), Stav (status badge).
- Rows are clickable → navigate to `/automation/meeting-tasks/{id}`.
- `PendingReview` rows visually emphasized (`bg-yellow-50`).
- Pagination controls (prev/next, "Strana X z Y (Z celkem)") visible only when `totalPages > 1`.

**Status badges:** `PendingReview` → yellow + Clock icon + "Ke kontrole"; `Approved` → green + CheckCircle + "Schvaleno"; `PartiallyApproved` → blue + CheckCircle2 + "Castecne". Unknown statuses fall back to gray with the raw status string.

**Acceptance criteria:**
- Changing a filter resets `page` to 1.
- Loading state renders "Nacitani..." row; empty state renders "Zadne zaznamy".
- Default page size is 20.

### FR-3: Meeting Task Detail / Validation Page
Create `frontend/src/pages/automation/MeetingTaskDetailPage.tsx` at route `/automation/meeting-tasks/:id`.

**Layout:**
- Back link → list page.
- Header: subject, `plaudCreatedAt` formatted cs-CZ, `plaudRecordingId`.
- Summary block (blue) showing `transcript.summary` with `whitespace-pre-wrap`.
- Tasks section header: "Navrhovane ulohy ({count})" + action buttons.
- Task cards styled by status: Approved (green tint), Rejected (gray + opacity-60 + line-through title), Pending (white).
- Submit footer button "Odeslat do TODO ({approvedCount})", disabled when `approvedCount === 0`.

**Per-task interactions:**
- Click body of a `Pending` task → enter inline edit mode (title, description, assignee, due date).
- Approve / Reject buttons visible only when task is `Pending`.
- `isManuallyAdded` shows "rucne pridano" tag.
- `externalTaskId` present → "Odeslano do TODO" indicator.

**Bulk / global actions:**
- "Schvalit vse ({pendingCount})" — iterates pending tasks calling `updateStatus` with `Approved`. Visible only when `pendingCount > 0`.
- "Pridat ulohu" — reveals inline form (title, description, assignee, due date). Submit disabled until title and assignee filled.
- "Odeslat do TODO" — opens confirmation modal; on confirm calls `useSubmitToTodo`. Modal shows pending/error state and disables confirm while `isPending`.

**Acceptance criteria:**
- Empty `dueDate` strings serialize as `null` in update/add payloads.
- After save/add/submit/status change, detail data refreshes via query invalidation (no manual refetch).
- Loading state: "Nacitani..."; missing transcript: "Zaznam nenalezen".
- Confirmation modal shows red error text on submit failure.

### FR-4: Routing & Navigation Wiring
Modify `frontend/src/App.tsx` and `frontend/src/components/Layout/Sidebar.tsx`.

**Acceptance criteria:**
- `App.tsx` registers `/automation/meeting-tasks` → `MeetingTasksPage` and `/automation/meeting-tasks/:id` → `MeetingTaskDetailPage`. Routes placed adjacent to existing `/automation/background-tasks` route.
- Imports for both pages added.
- Sidebar "automatizace" section gains a "Meeting Tasks" item (`id: "meeting-tasks"`, `href: "/automation/meeting-tasks"`) directly after the "hangfire" item.

### FR-5: Backend Configuration Section
Add a `MeetingTasks` section to `backend/src/Anela.Heblo.API/appsettings.json`:

```json
"MeetingTasks": {
  "ApiKey": "",
  "TodoListName": "Meeting Actions"
}
```

**Acceptance criteria:**
- `ApiKey` left empty in committed config (resolved from user secrets / env vars).
- `TodoListName` defaults to `"Meeting Actions"`.
- `dotnet build` and `dotnet format` succeed after the change.

### FR-6: Build & Test Verification
Final wiring step validates the whole subtask.

**Acceptance criteria:**
- `cd frontend && npm run build` succeeds with no TypeScript errors.
- `cd frontend && npm run lint` reports no new errors.
- `dotnet build backend/` succeeds.
- `dotnet test backend/test/Anela.Heblo.Tests/ --filter "FullyQualifiedName~MeetingTasks"` passes.
- `dotnet format backend/` produces no diffs (or auto-fixes cleanly).

## Non-Functional Requirements

### NFR-1: Performance
- List queries default to 20 items per page to keep payloads small.
- React Query handles caching and deduplication; no manual cache wiring beyond `invalidateQueries`.
- Bulk approve fires sequential `await` mutations (acceptable for typical N < 20 tasks per meeting); no parallel `Promise.all` to avoid race conditions in the optimistic cache.

### NFR-2: Security
- All requests authenticated via `getAuthenticatedApiClient()` (Entra ID-bearing fetch).
- No secrets in frontend code or committed config — backend `MeetingTasks:ApiKey` populated via user secrets or environment variables.
- Absolute URLs constructed from `apiClient.baseUrl` to avoid dev-server proxy mishaps (per CLAUDE.md project rule).

### NFR-3: Localization
UI strings are Czech (no diacritics, matching brief). Date formatting uses `cs-CZ` locale.

### NFR-4: Accessibility
- Action icon buttons (approve/reject/pagination) carry `title` attributes for screen reader/tooltips.
- Modal uses `z-50` overlay; focus is not trapped (acceptable per existing patterns in this codebase, see Open Questions).

## Data Model

### Frontend types (exported from `useMeetingTasks.ts`)

**`ProposedTaskDto`**
| Field | Type |
|---|---|
| id | string |
| title | string |
| description | string |
| assignee | string |
| dueDate | string \| null (ISO) |
| status | "Pending" \| "Approved" \| "Rejected" |
| externalTaskId | string \| null |
| isManuallyAdded | boolean |

**`MeetingTranscriptDto`**
| Field | Type |
|---|---|
| id | string |
| subject | string |
| summary | string |
| plaudRecordingId | string |
| plaudCreatedAt | string (ISO) |
| status | "PendingReview" \| "Approved" \| "PartiallyApproved" |
| receivedAt | string (ISO) |
| reviewedAt | string \| null |
| reviewedByUser | string \| null |
| taskCount | number |
| approvedTaskCount | number |
| rejectedTaskCount | number |
| tasks | ProposedTaskDto[] |

**Response envelopes:** `TranscriptListResponse` (items, totalCount, pageNumber, pageSize, totalPages), `TranscriptDetailResponse` (transcript), `SubmitToTodoResponse` (successCount, failedCount, errors[]), `AddProposedTaskResponse` (task).

## API / Interface Design

Frontend consumes these existing backend endpoints (defined by sibling subtasks):

| Method | Path | Hook |
|---|---|---|
| GET | `/api/meeting-tasks?statusFilter=&pageNumber=&pageSize=` | `useMeetingTasksList` |
| GET | `/api/meeting-tasks/{id}` | `useMeetingTaskDetail` |
| PUT | `/api/meeting-tasks/{transcriptId}/tasks/{taskId}` | `useUpdateProposedTask` |
| PUT | `/api/meeting-tasks/{transcriptId}/tasks/{taskId}/status` | `useUpdateProposedTaskStatus` |
| POST | `/api/meeting-tasks/{transcriptId}/tasks` | `useAddProposedTask` |
| POST | `/api/meeting-tasks/{transcriptId}/submit` | `useSubmitToTodo` |

**Routes added:**
- `/automation/meeting-tasks` — list
- `/automation/meeting-tasks/:id` — detail

**Sidebar entry:** "Meeting Tasks" under the existing "automatizace" group.

## Dependencies
- `@tanstack/react-query` (already in use across `frontend/src/api/hooks/`).
- `react-router-dom` (`useNavigate`, `useParams`, `<Route>`).
- `lucide-react` icons: `Clock`, `CheckCircle`, `CheckCircle2`, `ChevronLeft`, `ChevronRight`, `ArrowLeft`, `Check`, `X`, `Plus`, `Send`, `CheckCheck`.
- Existing `getAuthenticatedApiClient()` helper from `frontend/src/api/client`.
- Backend endpoints from subtasks 1–5 must be deployed/available for the UI to function (compile-time independent).
- Microsoft Graph TODO integration (subtask covered elsewhere) required only for the live "Odeslat do TODO" path; UI compiles and renders without it.

## Out of Scope
- Backend handlers, persistence, and Graph integration (covered by sibling subtasks 1–9).
- Authentication/authorization changes — relies on existing Entra ID flow.
- Optimistic UI updates beyond React Query's default invalidation cycle.
- Bulk reject, undo, or audit-log views.
- Mobile-specific layout tuning beyond Tailwind's default responsive behavior.
- E2E Playwright tests for this UI (would land under the epic's testing subtask, not this one).
- Internationalization framework — strings are inline Czech.
- Generating the TypeScript client from OpenAPI for these endpoints — hooks hand-write the contract per the brief.

## Open Questions
None.

## Status: COMPLETE