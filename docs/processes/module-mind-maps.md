---
process: module-mind-maps
kind: module
module: mind-maps
summary: Living project mind maps (Myšlenkové mapy) that Claude evolves meeting by meeting from recorded meeting transcripts, editable by hand with auto-locking and full version history.
owns: []
verified_at: "5e993f9e2"
related:
  - job-mindmap-update
---

# Mind maps (Myšlenkové mapy)

## Purpose
A long-term management overview above individual meetings. A map represents Anela's projects and
workstreams (initiatives, sub-tracks, status, owners). Over time users attach recorded meetings
(Porady, from the meetings module) to the map; for each one Claude reads the summary and full
transcript and updates the map — new branches, dated progress notes, status changes, finished
work moved to „Hotovo", postponed work to „Odloženo". People can edit the map by hand; any node
they change is locked against further AI rewrites. Every AI update and every restore keeps a
snapshot, so a bad update can be rolled back.

It answers "where do our projects stand, and what moved since the last meeting?" for management,
and is meant to be shown in meetings.

## Users & screens
Permission feature `Anela_MindMaps` ("Myšlenkové mapy"): `anela.mind_maps.read` to view,
`anela.mind_maps.write` for every change. Sidebar: Anela → Myšlenkové mapy.

| Route | What users do there |
|---|---|
| `/automation/mind-maps` | List of maps (name, description, status, meeting count, last update), "Nová mapa" (name + description), "Smazat". |
| `/automation/mind-maps/{id}` | Map editor (mind-elixir canvas): edit nodes in a dialog, drag, collapse; "Uložit" (⌘S); "Regenerovat" when the map is `Failed` or has a pending meeting; side panel "Porady" (attach "Připojit poradu" / detach "Odpojit", state Zpracováno / Čeká) and "Historie" (versions, "Obnovit"). Read-only while the map is `Updating`; polls every 3 s meanwhile. |

No MCP tools and no dashboard tiles expose mind maps.

## Processes
- `job-mindmap-update` — Claude merges each pending attached meeting into the map (guarded,
  versioned). Enqueued Hangfire job on attach and on Regenerate; no schedule.

User actions that only touch Heblo's own tables (no process doc):
- **Create** (`POST /api/mind-maps`): new map with a single root node titled with the map name,
  status `Idle`.
- **Delete** (`DELETE /api/mind-maps/{id}`): removes the map, its meeting links and all versions
  (cascade). Meeting transcripts themselves are untouched.
- **Detach meeting** (`DELETE /api/mind-maps/{id}/meetings/{meetingId}`): removes the link only;
  content Claude already derived from that meeting stays in the map.
- **Save** (`PUT /api/mind-maps/{id}/document`): refused while `Updating` (3501). The submitted
  document is validated (`MindMapDocumentValidator`; invalid → 3503), the root id must not change,
  then `MindMapLockService.ApplyUserEdit` runs:
  - a node whose title, notes or owner changed gets `lockedBy` = the user's email (locks are never
    cleared; the client cannot set or clear them, nor change `sourceMeetingIds`);
  - a node with an id unknown to the server is new: fresh id, locked by the user, no provenance;
  - a node missing from the submission is a deletion: its title is added to `suppressedNodes`
    (with `deletedBy`), so Claude will not re-create a node of that title (case-insensitive).
  Saves do **not** create a version.
- **Restore version** (`POST /api/mind-maps/{id}/versions/{n}/restore`): refused while `Updating`.
  Snapshots the current document as a new version (`TriggerMeetingId` null), then makes version
  `n`'s JSON current. Restoring does not reset which meetings are marked processed.

**Versioning.** `MindMapVersions` rows are created only (1) by the update job, before each
meeting's change, holding the pre-update document and the triggering meeting, and (2) by restore,
holding the document being replaced. Version numbers increase by 1 per map
(`UX_MindMapVersions_MindMapId_VersionNumber`). So "version N" = the map as it was just before the
change that followed it; the newest state is always `MindMaps.CurrentJson`, not a version row.

## Data owned
All in `Heblo_V3`, schema `public` (migration `20260810105440_AddMindMapsTables`):
- `MindMaps` — one map: `Name` (≤ 200), `Description` (≤ 2000), `Status` (`Idle`/`Updating`/`Failed`,
  stored as string), `CurrentJson` (jsonb, the whole document), `LastError`, `CreatedAt`, `UpdatedAt`.
- `MindMapMeetings` — a meeting attached to a map: `MeetingTranscriptId`, `AttachedAt`,
  `ProcessedAt` (null = waiting for Claude). Unique per map + meeting. Cascade-deleted with the map
  and with the meeting transcript.
- `MindMapVersions` — snapshot: `VersionNumber`, `Json` (jsonb, full document), `CreatedAt`,
  `TriggerMeetingId` (null for restore snapshots).

Document (`MindMapDocument`, camelCase JSON): `schemaVersion` (1), `rootNodeId`, `nodes[]` (`id`,
`parentId`, `title`, `notes`, `status` = `active`/`done`/`blocked`/`idea`, `owner`, `lockedBy`,
`sourceMeetingIds`, `position`, `collapsed`), `suppressedNodes[]` (`title`, `deletedBy`).

## External systems
- **Anthropic Claude** (Messages API, model from `KnowledgeBase:ChatModel`, default
  `claude-sonnet-4-6`) — Heblo → Anthropic: current map + meeting summary + full transcript;
  reply = next map. Only in `job-mindmap-update`. Staging uses a stub (`MindMaps:UseStubUpdater`).

## Dependencies
- **Meetings (MeetingTasks module)** — source of `MeetingTranscript` (subject, `PlaudCreatedAt`,
  participants, summary, raw transcript), recorded with Plaud and ingested by that module. Its
  `IMeetingAccessGuard` decides which meetings a user may attach; the attach dialog lists meetings
  through the meetings list API (up to 200).
- **Anthropic adapter** — keyed `IChatClient` `"mindmap-updater"`.
- No other module reads mind map data.

## Known quirks
- **Save-vs-job race has no concurrency token** — only the `Updating` status check protects a manual
  save from colliding with an AI update; a collision is a silent last-writer-wins (memory note
  `gotcha_mindmap_concurrency_no_token`, 2026-08-10). Details in `job-mindmap-update`.
- **Manual edits are not versioned on their own.** A save overwrites `CurrentJson` without a
  snapshot; hand edits only survive in history if an AI update or a restore snapshots them later.
  A mistaken save cannot be undone from "Historie" unless such a snapshot exists.
- **Versions grow without limit** — every AI update stores a full copy of the document; there is no
  retention or cleanup.
- **Locks are permanent** — once a person edits a node's title, notes or owner, Claude can never
  change those fields again (only status, position in the tree and children); there is no unlock.
- **Deleting suppresses by title forever** — a deleted node's title blocks Claude from ever creating
  a node with the same title on that map, even years later.
- **Meeting privacy** — a map built from a private or restricted meeting shows its derived content
  and the meeting subject to everyone with `anela.mind_maps.read`.
- **Delete while updating** is not blocked; the running job then fails to save.
- Removing a meeting transcript cascades away its `MindMapMeetings` link, but node
  `sourceMeetingIds` and version `TriggerMeetingId` keep pointing at the deleted id (the version
  list then shows no subject).
- The detail route `/automation/mind-maps/:id` has no frontend permission guard (the list route
  does); the API still enforces `anela.mind_maps.read`.

## Code entry points
- `backend/src/Anela.Heblo.API/Controllers/MindMapsController.cs` — all endpoints and permissions
- `backend/src/Anela.Heblo.Application/Features/MindMaps/MindMapsModule.cs` — DI, stub vs Claude updater
- `backend/src/Anela.Heblo.Application/Features/MindMaps/Services/MindMapLockService.cs` — auto-lock and tombstones on save
- `backend/src/Anela.Heblo.Application/Features/MindMaps/Model/` — document model, validator, JSON options
- `backend/src/Anela.Heblo.Application/Features/MindMaps/UseCases/` — one folder per endpoint
- `backend/src/Anela.Heblo.Persistence/MindMaps/` — EF configurations and repository
- `frontend/src/components/pages/automation/mindmaps/` — list, editor, side panel
- `frontend/src/api/hooks/useMindMaps.ts` — API hooks, 3 s polling while `Updating`
