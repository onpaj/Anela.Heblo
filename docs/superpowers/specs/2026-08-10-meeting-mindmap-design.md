# Meeting Mind Maps — Design

**Date:** 2026-08-10
**Status:** Approved by user (brainstorming session)
**Module:** MindMaps (new vertical slice)

## Purpose

A long-term management layer above the MeetingTasks module. The user creates a mind map
representing **projects and workstreams** (initiatives, sub-tracks, status, owners) and attaches
meetings to it over time. Each attached meeting's transcript is processed by Claude, which evolves
the map: new branches appear, statuses update, progress accumulates. Users can edit the map by
hand; user-edited nodes are automatically locked and the LLM must never rewrite them.

## Decisions made during brainstorming

| Question | Decision |
|---|---|
| Node semantics | Projects & workstreams (management overview) |
| Edit protection | Auto-lock on edit: user-changed nodes are locked; LLM may still add children and change status, never title/notes/owner |
| LLM runner | In-app Claude service via the existing Anthropic `IChatClient` adapter; behavior defined by a skill-style markdown prompt file in the repo |
| UI | Full editor via React Flow (`@xyflow/react` + `dagre` auto-layout) — new frontend dependencies |
| Trigger | Automatic on attach (background job) + manual Regenerate button |
| Update mechanism | Full-map rewrite by the LLM, with a deterministic server-side guard pass enforcing locks, plus version snapshots for rollback |

## Data model

New entities in `Domain/Features/MindMaps/`:

- **`MindMap`** — `Id` (Guid), `Name`, `Description`, `CreatedAt`, `UpdatedAt`,
  `Status` (`Idle` / `Updating` / `Failed`), `CurrentJson` (jsonb, the whole map document),
  `LastError` (nullable).
- **`MindMapMeeting`** — join to `MeetingTranscript`: `MindMapId`, `MeetingTranscriptId`,
  `AttachedAt`, `ProcessedAt` (null = pending LLM processing). Many-to-many: one meeting can feed
  multiple maps.
- **`MindMapVersion`** — snapshot of the JSON taken before every LLM update and before every
  restore: `Id`, `MindMapId`, `VersionNumber`, `Json`, `CreatedAt`, `TriggerMeetingId` (nullable).

Enums stored as strings (`HasConversion<string>()`), matching MeetingTasks conventions.
DTOs are classes, never records.

### Map document schema (`CurrentJson`)

```json
{
  "schemaVersion": 1,
  "rootNodeId": "a1b2",
  "nodes": [
    {
      "id": "a1b2",
      "parentId": null,
      "title": "Web relaunch",
      "notes": "Longer free text",
      "status": "active",
      "owner": "Ondra",
      "lockedBy": null,
      "sourceMeetingIds": ["<meeting guid>"],
      "position": { "x": 120, "y": 40 },
      "collapsed": false
    }
  ],
  "suppressedNodes": [ { "title": "…", "deletedBy": "…" } ]
}
```

- **Tree, not free graph**: every node except the root has a `parentId`; exactly one root per map.
- `status` ∈ `active | done | blocked | idea`.
- **UI/system metadata** — `position`, `collapsed`, `lockedBy` — is never writable by the LLM.
  The server strips it before the LLM call and merges it back by node id afterwards.
  `position: null` means the frontend auto-layouts the node.
- **`suppressedNodes`** is the tombstone list: titles of user-deleted nodes, passed to the LLM as
  "do not recreate", and enforced by the guard pass.
- **Auto-lock**: on user save the server diffs the submitted document against the current one.
  Any node whose `title`/`notes`/`owner` changed, or that the user newly added, gets
  `lockedBy = <user email>`. Repositioning or collapsing a node does not lock it.

### Access

- Feature-gated by a new feature flag `Anela_MindMaps` (`[FeatureAuthorize]`).
- Maps are visible to every user with the feature enabled — no per-map access grants in v1.
- Attaching a meeting requires that the current user can access that meeting under the existing
  MeetingTasks access rules (`MeetingAccessGuard`).

## Backend

Vertical slice mirroring MeetingTasks:

```
Domain/Features/MindMaps/            MindMap, MindMapMeeting, MindMapVersion, enums, IMindMapRepository
Persistence/MindMaps/                configurations (jsonb for CurrentJson/Json), repository, migration
Application/Features/MindMaps/
    MindMapsModule.cs                DI registration (AddMindMapsModule)
    MindMapsOptions.cs               options + validation
    Contracts/                       DTOs (classes)
    Services/                        ClaudeMindMapUpdater, MindMapGuard (guard pass), document diff/lock service
    Prompts/mindmap-update-skill.md  the LLM system prompt, shipped as a content file
    UseCases/<Name>/                 Request/Handler/Response per use case
API/Controllers/MindMapsController.cs
```

### Endpoints (`api/mind-maps`)

| Method | Route | Purpose |
|---|---|---|
| GET | `/` | List maps (name, meeting count, status, updated) |
| POST | `/` | Create map (name + optional description → document with a single root node) |
| GET | `/{id}` | Detail: document + attached meetings + version list |
| DELETE | `/{id}` | Delete map |
| POST | `/{id}/meetings` | Attach meeting (validates meeting access), triggers update |
| DELETE | `/{id}/meetings/{meetingId}` | Detach meeting (does not rewrite the map) |
| PUT | `/{id}/document` | Save user edits; runs auto-lock diff; returns conflict error while `Updating` |
| POST | `/{id}/regenerate` | Re-run pending/failed meetings |
| POST | `/{id}/versions/{versionNumber}/restore` | Rollback (snapshots current first) |

All responses inherit `BaseResponse`. New `ErrorCode`s registered in the module range bucket in
`ErrorHandlingTests` and translated in `i18n.ts` (Czech).

### Update pipeline

1. Attach creates the join row, sets `Status = Updating`, enqueues a Hangfire job with the map id.
2. The job processes pending meetings **one at a time, ordered by meeting date** (`PlaudCreatedAt`),
   each iteration building on the previous result.
3. Per meeting it calls a keyed Anthropic `IChatClient` (`"mindmap-updater"`, registered like
   `"meeting-extractor"`) with:
   - system prompt loaded from `Prompts/mindmap-update-skill.md`;
   - current document stripped of UI metadata, locked nodes marked `"locked": true`;
   - the suppressed-nodes list;
   - the meeting's `Subject`, `Summary`, `Participants` (bounds the names the prompt allows
     in `owner`; omitted when empty), `RawTranscript`.
4. The model returns the full updated document. The server runs the **guard pass** (pure C#):
   - schema + tree validation: single root, no cycles, no orphan `parentId`s;
   - locked nodes: `title`/`notes`/`owner` restored from the previous version if changed;
     re-inserted (re-parented to nearest surviving ancestor) if deleted. Status changes and new
     children under locked nodes are allowed;
   - suppressed titles that reappeared are removed;
   - new nodes get server-assigned ids; UI metadata merged back by id (new nodes:
     `position: null`);
   - snapshot previous version → save → mark join `ProcessedAt` → `Status = Idle`.
5. **Failure handling**: unparseable/invalid output gets one automatic retry with the error
   appended to the prompt. A second failure sets `Status = Failed` + `LastError`, leaves the
   meeting pending, and never overwrites the map with a bad document. UI surfaces the error with
   a Regenerate button.

### First generation

Creating a map produces a document with one root node named after the map. Attaching the first
meeting runs the same pipeline — there is no special "initial generation" path.

## Frontend

New dependencies: `@xyflow/react` (React Flow), `dagre` (auto-layout).

- **`/automation/mind-maps`** — list page: name, meeting count, last updated, status badge
  (Idle / Updating… / Failed), create dialog.
- **`/automation/mind-maps/:id`** — editor:
  - React Flow canvas: pan/zoom, drag to reposition (persisted), double-click rename, node
    context actions (add child, delete, change status), collapse/expand. Locked nodes show a lock
    badge; status renders as a color accent per the design system
    (`docs/design/ui_design_document.md`).
  - Side panel tabs: **Meetings** (attached list with processed/pending state, attach dialog
    listing accessible MeetingTasks, detach), **Node** (notes/owner/status editor for selection),
    **History** (versions with restore).
  - Explicit **Save** with the unsaved-changes leave-guard pattern
    (cf. `MeetingReviewLeaveDialog`). While `Updating`, the canvas is read-only with a progress
    banner; the page polls via react-query until Idle/Failed.
- Hooks in `frontend/src/api/hooks/useMindMaps.ts`: query-key object, absolute URLs via
  `apiClient.baseUrl`, error codes read from caught `SwaggerException`.

## Testing

- **Backend unit tests (primary):** guard pass (lock restoration, deleted-locked-node
  reinsertion, suppressed-node removal, cycle/orphan rejection, metadata merge), auto-lock diff,
  handlers with mocked `IChatClient`, `BaseResponse` contract compliance.
- **Frontend:** hook tests + a component test of the save/lock flow (contexts mocked).
- **E2E:** one scenario in `frontend/test/e2e/mindmaps/`: create map → attach fixture meeting →
  wait for generation → rename node → verify lock badge. The staging/E2E configuration stubs the
  LLM response behind a config flag so the nightly run is deterministic.

## Out of scope (v1)

Per-map access grants, MCP tools for mind maps, real-time collaboration, non-tree cross-links,
map export. The skill-file prompt is designed so it can later be reused as a genuine Claude Code
skill if an external-agent path (via MCP) is added.
