# User-aware meeting task extraction + transcript display

**Date:** 2026-05-16
**Status:** Approved — ready for implementation planning

## Problem

Meeting tasks ingested from Plaud recordings have two gaps:

1. **Transcript not visible.** The raw transcript is already fetched and stored
   (`MeetingTranscript.RawTranscript`), but the detail page only shows the
   summary. There is no way to read the underlying transcript in the UI.

2. **Assignees are brittle freeform strings.** The extraction LLM emits a plain
   name (e.g. `"Andy"`). When tasks are submitted to Microsoft TODO,
   `GraphTodoService.ResolveUserIdAsync()` does an exact match
   (`/users?$filter=displayName eq '{name}'`). Informal names and nicknames
   never match, so the task fails to submit.

## Goal

Give the extraction LLM a known user directory so it can assign tasks to
canonical organisation users (real email addresses), and surface the stored
transcript in the UI.

## Non-goals

- No live Microsoft Graph user fetching for the directory — the directory is a
  static JSON file maintained in the repo.
- No Entra ID extension-attribute storage for aliases.
- No change to how summaries or transcripts are fetched from Plaud (already done).

## Design

### 1. User directory (static JSON)

A new file `meeting-users.json` bundled in the API project. Its path is
configurable via `appsettings` under `MeetingTasks:UserDirectoryPath`.

Format:

```json
[
  {
    "email": "andrea@anela.cz",
    "displayName": "Andrea Nováková",
    "aliases": ["Andy", "Andrea", "A."]
  }
]
```

A new cached service `IMeetingUserDirectory` loads the file once into memory
(same pattern as `GraphService`'s in-memory cache). It exposes:

- `IReadOnlyList<MeetingUser> GetAll()`
- `MeetingUser? Resolve(string nameOrAlias)` — case-insensitive match against
  `displayName` and `aliases`.

`MeetingUser` carries `Email`, `DisplayName`, `Aliases`.

The directory is maintained by editing the JSON file and committing it —
version-controlled, no Graph calls, no admin tooling.

### 2. Domain + persistence change

`ProposedTask` gains a nullable `string? AssigneeEmail`. The existing
`Assignee` field stays as the human-readable display name.

A manual EF Core migration adds one nullable column to the `ProposedTasks`
table (migrations are manual per project convention). Existing rows keep
`AssigneeEmail = null`.

### 3. Extraction (LLM)

`ClaudeMeetingTaskExtractor` receives `IMeetingUserDirectory` and injects the
user list (display names + aliases) into the system prompt. The LLM output
schema gains one field per task:

- `assignee` — display name (unchanged)
- `assigneeEmail` — the matched canonical email, or `null` when no confident
  match exists

`ExtractedTask` gains `AssigneeEmail`.

**Safety net:** in `IngestPlaudRecordingHandler`, if the LLM returns a name but
no email, call `directory.Resolve(name)` before persisting and fill the email
if it resolves.

### 4. API

- **New endpoint** `GET /api/meeting-tasks/users` — returns the directory
  entries for the frontend assignee dropdown.
- `MeetingTranscriptDto` gains `RawTranscript`. The entity already has it;
  `GetTranscriptDetailHandler` just passes it through.
- `ProposedTaskDto` and the add/update-task request contracts gain
  `AssigneeEmail`.

### 5. Submission

`GraphTodoService` resolves the user by `AssigneeEmail` directly
(`/users?$filter=mail eq '{email}'`) instead of the fuzzy `displayName` query.

Approved tasks with no `AssigneeEmail` are **skipped and reported** — this
reuses the existing partial-success path in `SubmitToTodoHandler` (resolvable
tasks are submitted, unresolved ones are returned in the response and the
transcript status reflects partial submission).

### 6. Frontend

- **Transcript:** a collapsible section (or Summary / Transcript tab) on
  `MeetingTaskDetailPage` shows `rawTranscript` as plain, pre-wrapped text — it
  is speaker-labeled plain text, not markdown, so no `ReactMarkdown`.
- **Assignee field:** the free-text assignee input in the edit and add-task
  forms becomes a dropdown populated from `GET /api/meeting-tasks/users`.
- **Warning badge:** any task with no `assigneeEmail` shows a
  "⚠ neznámý uživatel" badge, prompting a manual pick before submission.
- `ProposedTaskDto` and `TaskFormData` in the frontend gain `assigneeEmail`.

## Data flow

```
Plaud CLI ─► IngestPlaudRecordingHandler
                │  summary + transcript
                ▼
         ClaudeMeetingTaskExtractor ◄── IMeetingUserDirectory (meeting-users.json)
                │  ExtractedTask { assignee, assigneeEmail }
                ▼
         IngestPlaudRecordingHandler ── safety-net Resolve()
                │
                ▼  persist ProposedTask { Assignee, AssigneeEmail }
            ┌───────────────┐
            │ MeetingTranscript │ (RawTranscript already stored)
            └───────────────┘
                │
   detail page ◄┤  Summary (markdown) + Transcript (plain) + tasks
                │  assignee dropdown ◄── GET /meeting-tasks/users
                ▼
         SubmitToTodoHandler ─► GraphTodoService (resolve by mail)
                │  unresolved tasks skipped + reported (partial success)
                ▼
            Microsoft TODO
```

## Error handling

- **Missing/invalid `meeting-users.json`:** the directory service logs an error
  and falls back to an empty directory; extraction still works (LLM emits
  `assigneeEmail: null`), the UI just has no dropdown options.
- **LLM mismatch:** unmatched assignees persist with `AssigneeEmail = null` and
  are flagged in the UI; submission skips and reports them.
- **Graph resolution failure on submission:** existing per-task error handling
  in `SubmitToTodoHandler` applies — the task is reported as failed, others
  proceed.

## Testing

- `IMeetingUserDirectory`: alias/display-name resolution (case-insensitive,
  unknown name returns null), malformed-file fallback.
- `ClaudeMeetingTaskExtractor`: prompt includes the directory; output parsing
  maps `assigneeEmail`.
- `IngestPlaudRecordingHandler`: safety-net `Resolve()` fills email when LLM
  omits it.
- `GraphTodoService`: resolves by `mail` filter; unresolved assignee handled.
- Frontend: dropdown renders directory; warning badge shows for null email;
  transcript section toggles.

## Affected files (indicative)

- `backend/.../Domain/Features/MeetingTasks/ProposedTask.cs`
- `backend/.../Persistence/MeetingTasks/ProposedTaskConfiguration.cs` + migration
- `backend/.../Application/Features/MeetingTasks/Services/` — new
  `IMeetingUserDirectory` + implementation, `MeetingUser`
- `backend/.../Application/Features/MeetingTasks/Services/ClaudeMeetingTaskExtractor.cs`
- `backend/.../Application/Features/MeetingTasks/UseCases/IngestPlaudRecording/IngestPlaudRecordingHandler.cs`
- `backend/.../Application/Features/MeetingTasks/Services/GraphTodoService.cs`
- `backend/.../Application/Features/MeetingTasks/Contracts/` — DTOs
- `backend/.../Application/Features/MeetingTasks/UseCases/GetTranscriptDetail/`
- new `GET /meeting-tasks/users` use case + controller action
- `backend/src/Anela.Heblo.API/appsettings.json` + `meeting-users.json`
- `frontend/src/components/pages/automation/MeetingTaskDetailPage.tsx`
- `frontend/src/api/hooks/useMeetingTasks.ts`
</content>
</invoke>
