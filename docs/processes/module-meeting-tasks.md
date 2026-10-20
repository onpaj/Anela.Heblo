---
process: module-meeting-tasks
kind: module
module: meeting-tasks
summary: Meeting notes (Porady) — imports Plaud meeting recordings, lets Claude propose action items, has a manager review them and sends the approved ones to Microsoft Planner, with per-meeting visibility control.
owns: []
verified_at: "5e993f9e2"
related:
  - sync-plaud-recordings
  - feed-meeting-tasks-to-planner
---

# Meeting tasks (Porady)

## Purpose
Anela records its internal meetings on a **Plaud** voice recorder. This module brings every
recording into Heblo as a meeting note — transcript, Plaud's AI summary, participants — and
uses Claude to propose **action items** (who does what by when). A meeting manager reviews the
proposals, edits or adds tasks, approves or rejects them and sends the approved ones to
**Microsoft Planner** so the assignees see them in Microsoft 365. Managers also decide who else
may read each meeting. Claude (through MCP) can search and read the meetings the asking user
is allowed to see.

## Users & screens
Permissions: `anela.meetings.read` (feature `Anela_Meetings`, Read) to open the module;
`anela.meetings.write` for every change. Holders of `anela.meetings.write` are "meeting
managers" (`IMeetingAccessGuard.IsManager`) and see **all** meetings.

| Route | What happens there |
|---|---|
| `/automation/meeting-tasks` — sidebar "Porady" | List of meetings, newest recording first, 20 per page; search in subject + summary (optionally also the transcript, case-insensitive), filter by status; counts of tasks / approved / rejected |
| `/automation/meeting-tasks/:id` | Detail: summary, transcript, participants, proposed tasks (filter by person). Download summary / transcript as text. Managers: approve/reject/edit/add tasks, "Odeslat do TODO", mark reviewed, change access, re-import from Plaud, delete, and "explain" — select a fragment of the summary or a task and Claude quotes the part of the transcript it came from |

MCP tools (`MeetingTasksMcpTools`, read-only, require `anela.meetings.read`, same visibility
rules): `ListMeetings`, `GetMeetingSummary`, `GetMeetingTranscript`, `GetMeetingTasks`.

## Processes
- `sync-plaud-recordings` — Hangfire `plaud-polling` every 5 min (off by default): Plaud CLI →
  `MeetingTranscripts` + Claude-extracted `ProposedTasks`; also the manual re-import button.
- `feed-meeting-tasks-to-planner` — button "Odeslat do TODO": approved tasks → Microsoft Graph
  Planner tasks; recomputes meeting status.

Plain CRUD / on-demand actions (no own doc), all under `api/meeting-tasks` and requiring
`anela.meetings.write` unless noted:
- List / detail / meeting-users (`GET`, read permission) — the user list comes from
  `meeting-users.json`, not from Entra ID.
- Edit a task (`PUT {id}/tasks/{taskId}`), set task status Pending/Approved/Rejected
  (`PUT …/status`), add a manual task (`POST {id}/tasks`, `IsManuallyAdded = true`; the UI
  requires a title and a known assignee).
- Toggle meeting review status (`PUT {id}/status`): only `PendingReview` ↔ `Approved`; Approved
  stamps `ReviewedAt` / `ReviewedByUser`. `PartiallyApproved` is set only by the Planner submit.
- Change access (`PUT {id}/access`): `Private` (managers only — the default for every new
  meeting), `Public` (everyone with read permission), `Restricted` (listed people only; at
  least one, each must be in `meeting-users.json`). Replaces all grants in `MeetingAccessGrants`.
- Explain (`POST {id}/explain`): one Claude call with the full transcript and the selected
  text; returns the quoted passage + Czech explanation; nothing is stored. Any failure returns
  "Vysvětlení není k dispozici.".
- Delete (`DELETE {id}`, managers): removes the meeting, its tasks, grants and Mind-map links
  (cascade) and writes a tombstone to `DeletedPlaudRecordings` so polling never re-imports it.
  Planner tasks already created stay in Planner.

## Data owned
All in `public` schema of the Heblo DB:
- `MeetingTranscripts` — one Plaud recording: `PlaudRecordingId` (bare 32-hex id, unique),
  `PlaudCreatedAt` (date), `Subject`, `Summary` (Markdown), `RawTranscript`, `Participants`
  (jsonb list of names), `Status` (`PendingReview`/`Approved`/`PartiallyApproved`, stored as
  text), `ReceivedAt`, `ReviewedAt`, `ReviewedByUser`, `AccessLevel` (text, default `Private`).
- `ProposedTasks` — one proposed action item: title, description, `Assignee` (name as spoken),
  `AssigneeEmail`, `DueDate`, `Status` (`Pending`/`Approved`/`Rejected`), `ExternalTaskId`
  (Planner task id once sent), `IsManuallyAdded`. Cascade-deleted with the meeting.
- `MeetingAccessGrants` — one person allowed to read a `Restricted` meeting (`UserEmail`,
  display name, who granted it and when); unique per meeting + e-mail.
- `DeletedPlaudRecordings` — tombstone per deleted recording (id, when, by whom; no content).
- File `~/.plaud/tokens.json` in the container — current Plaud OAuth tokens.

## External systems
- **Plaud** (read) — `@plaud-ai/cli` run as a child process: `recent --days N`, `file`,
  `transcript`, `summary`; OAuth token refresh at `platform.plaud.ai`. See `sync-plaud-recordings`.
- **Anthropic Claude** (read/derive) — task extraction (`sync-plaud-recordings`) and the
  explain action; shared chat client, model from `KnowledgeBase:ChatModel`.
- **Microsoft Graph** (write) — `GET /users` and `POST /planner/tasks` + `PATCH …/details`.
  See `feed-meeting-tasks-to-planner`.
- **Azure Key Vault** (write) — secret `Plaud--TokensJson` kept current with the rotating Plaud token.

## Dependencies
- Reads: Authorization (current user, `anela.meetings.*` roles), the shared Anthropic adapter,
  the shared `MicrosoftGraph` HttpClient / Microsoft Identity token acquisition, Hangfire
  recurring-job switch.
- Read by: **MindMaps** — a mind map can attach a meeting (`MindMapMeetings`, cascade on
  meeting delete; access checked with the same guard) and its updater feeds the meeting to
  Claude; MCP server (tools above).

## Known quirks
- **New meetings are Private**, so read-only users see nothing until a manager opens access.
  The access dialog describes Public as "vidí všichni přihlášení uživatelé", but in fact only
  users with `anela.meetings.read` can open the module.
- **Per-meeting checks on write endpoints are moot**: every write endpoint requires
  `anela.meetings.write`, and those users are managers who pass every access check. Access
  levels only matter for read-only users and MCP.
- **Explain needs write permission** even though it changes nothing.
- **Inaccessible meetings return 404, not 403**, so a user cannot tell "does not exist" from
  "not allowed".
- **The detail route has no frontend route guard** (`App.tsx`), unlike the list; the API still
  enforces permissions.
- **Re-import error text is always "Nahrávka pravděpodobně ještě není zpracována na straně
  Plaud"**, even when the real cause was a Claude extraction failure.
- **People list is a static file** (`backend/src/Anela.Heblo.API/meeting-users.json`, 21
  people with aliases), deployed with the app. It drives assignee matching, the assignee
  picker, restricted-access grants and Planner submit.
- Slow Plaud polling has starved the single Hangfire worker in production (2026-09-29) — see
  `sync-plaud-recordings`.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/MeetingTasksModule.cs` — DI, options validation
- `backend/src/Anela.Heblo.API/Controllers/MeetingTasksController.cs` — all endpoints and permissions
- `backend/src/Anela.Heblo.API/MCP/Tools/MeetingTasksMcpTools.cs` — MCP tools
- `backend/src/Anela.Heblo.Application/Features/MeetingTasks/Services/MeetingAccessGuard.cs` — visibility rules
- `backend/src/Anela.Heblo.Persistence/MeetingTasks/MeetingTranscriptRepository.cs` — list filters, re-import task replace, delete + tombstone
- `backend/src/Anela.Heblo.Persistence/MeetingTasks/*Configuration.cs` — tables and indexes
- `frontend/src/components/pages/automation/MeetingTasksPage.tsx`, `MeetingTaskDetailPage.tsx` — UI
