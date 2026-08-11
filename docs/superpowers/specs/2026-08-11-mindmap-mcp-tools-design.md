# Mind Map MCP Tools — Design

**Date:** 2026-08-11
**Status:** Approved by user (brainstorming session)
**Module:** MindMaps (extends the existing vertical slice) + API/MCP

## Purpose

Let an external agent — Claude app or Claude Code — read a mind map, discuss it in a normal
conversation, and write the conclusions back into Heblo. The reasoning happens outside the app
(cross-referencing whatever the user brings to the chat); Heblo stays the store of record and
keeps enforcing its own rules on every write.

The v1 mind map design
(`docs/superpowers/specs/2026-08-10-meeting-mindmap-design.md`) parked MCP tools as out of scope
with the note that an external-agent path could be added later. This is that path.

## Decisions made during brainstorming

| Question | Decision |
|---|---|
| Tool scope | Read + node editing. Lifecycle (create map, attach meeting, regenerate, restore, delete) stays in the web UI. |
| Write shape | One batched-patch tool taking a list of operations, applied atomically server-side. No whole-document rewrite by the model. |
| Lock semantics | An MCP write is a user edit: touched nodes auto-lock under the caller's email, exactly as a web-UI save does. |
| Read shape | Compact outline by default, raw document JSON on request. No separate node-detail tool. |
| Write guards | A `MindMapVersion` snapshot before every write, plus a revision token that rejects stale writes. No dry-run, no operation cap. |
| Guidance | Tool `[Description]` text and the outline output itself. No MCP prompt, no shipped skill file. |

## Tool surface

Three tools in `API/MCP/Tools/MindMapMcpTools.cs`, registered in `McpModule.cs` via
`.WithTools<MindMapMcpTools>()`. All gate on the `Anela_MindMaps` feature through
`ICurrentUserService.EnsureFeatureAccess` — `AccessLevel.Read` for the two read tools,
`AccessLevel.Write` for the write tool.

### `ListMindMaps()`

Returns `id`, `name`, `description`, `status`, `meetingCount`, `updatedAt`, `revision` per map.
Thin wrapper over `GetMindMapListRequest`.

### `GetMindMap(mapId, format = "outline")`

- `format: "outline"` (default) — deterministic text tree, cheap to load and natural to talk about.
- `format: "json"` — the raw `DocumentJson` from `GetMindMapDetailRequest`, for bulk restructuring
  where the outline is not enough.

Outline format:

```
# Web relaunch
status: idle | meetings: 7 | updated: 2026-08-09T14:22:03Z | revision: 638912345678901234

a1b2  Web relaunch  [active]
  c3d4  Nový e-shop  [active]  @Ondra  (locked)
        notes: Migrujeme na Shoptet, spuštění Q4…
    e5f6  Migrace produktů  [done]  @Jana  (2 meetings)
    g7h8  Platební brána  [blocked]
  i9j0  Obaly  [idea]

suppressed (do not re-create): "Starý blog", "Newsletter v2"
```

Rules:

- Two spaces of indent per depth level; children in document order under their parent.
- `notes` on its own line, indented to the title column, truncated at 200 characters with `…`;
  omitted when empty.
- `(locked)` when `lockedBy` is set. `@owner` when `owner` is set.
- `(N meetings)` is `sourceMeetingIds.Count`, shown only when non-zero — the caller can drill into
  a node's origin with the existing `GetMeetingTranscript` tool.
- The `suppressed` line is omitted when the list is empty.
- `status` / `meetings` / `updated` / `revision` come from the map entity, not the document.

The outline and the `[Description]` attributes are where the map's conventions reach the model:
the status vocabulary, what a lock means, and that suppressed titles should not be re-created.

### `ApplyMindMapChanges(mapId, revision, operations[])`

`operations` is a flat DTO array (a class, never a record — the MCP SDK generates the JSON schema
from it, and the repo bans records in generated contracts):

```csharp
public class MindMapOperationDto
{
    public string Op { get; set; }          // addNode | updateNode | moveNode | deleteNode
    public string? NodeId { get; set; }
    public string? ParentId { get; set; }
    public string? TempParentId { get; set; }
    public string? TempId { get; set; }
    public string? NewParentId { get; set; }
    public string? Title { get; set; }
    public string? Notes { get; set; }
    public string? Status { get; set; }
    public string? Owner { get; set; }
}
```

| op | required | optional | rules |
|---|---|---|---|
| `addNode` | `parentId` or `tempParentId`, `title` | `notes`, `status` (default `active`), `owner`, `tempId` | the server assigns the real id; `tempId` may be referenced as `tempParentId` by a later op in the same call, so one call can build a subtree |
| `updateNode` | `nodeId` | `title`, `notes`, `status`, `owner` | omitted field = unchanged; `""` = cleared (`title` may not be cleared) |
| `moveNode` | `nodeId`, `newParentId` | — | rejects moving the root, moving a node under itself or its own descendant, and unknown parents |
| `deleteNode` | `nodeId` | — | cascades to the whole subtree; every removed title is tombstoned; rejects the root |

Operations apply in array order. The call is **all-or-nothing**: the first invalid operation
rejects the whole batch with its index and nothing is written.

UI metadata is never written over MCP: a new node gets `position: null` (the editor auto-layouts
it) and `collapsed: false`, and existing nodes keep the position and collapse state they have.

Returns: the refreshed outline, the new `revision`, a `tempId → real id` map, and counts
(`added` / `updated` / `moved` / `deleted`, deletions including cascaded descendants).

## Semantics

### Locking — an MCP write is a user edit

The applier builds the target document and hands it to the existing
`MindMapLockService.ApplyUserEdit(current, submitted, callerEmail)`. That gives MCP writes exactly
the web UI's semantics with no new lock logic:

- a node whose `title` / `notes` / `owner` changed gets `lockedBy = <caller email>`, so the meeting
  update job can never rewrite what the user decided in chat;
- removed nodes become `suppressedNodes` tombstones;
- `sourceMeetingIds` and existing locks survive by node id;
- client-supplied `lockedBy` values are ignored.

Re-adding a previously suppressed title is allowed (the tombstone list only constrains the LLM
update path), and the tombstone stays — current `ApplyUserEdit` behaviour, unchanged here.

### Revision token

`revision` is `MindMap.UpdatedAt.Ticks` rendered as a string. Every existing write path bumps
`UpdatedAt` — the UI save, the update job, and version restore — so it is a valid change marker
without a schema change, and it is testable on the InMemory provider (unlike the `xmin` route
previously considered and deferred).

`GetMindMap` and `ListMindMaps` hand it out; `ApplyMindMapChanges` must send it back. A mismatch
is rejected with `MindMapRevisionMismatch` and a message telling the caller to re-read the map.

Residual race: the comparison and the write are not a single atomic database operation, so a job
committing between the check and the save is still theoretically possible. The `Updating` status
check covers the realistic window (the job holds that status for its whole run), and the version
snapshot makes any loss recoverable. A true optimistic-concurrency token remains available later
if this ever bites.

### Concurrency with the update job

`Status == Updating` rejects the write with `MindMapUpdateInProgress` and a retry-later message,
mirroring `SaveMindMapDocumentHandler`.

### Version snapshot

Every successful MCP write snapshots the pre-change `CurrentJson` as a `MindMapVersion` with
`TriggerMeetingId = null`, numbered via `IMindMapRepository.GetNextVersionNumberAsync`. A bad edit
is then one click to undo from the History tab.

Note the resulting asymmetry, deliberately accepted: web-UI saves still do not snapshot; only LLM
updates, restores, and now MCP writes do.

### No REST route

The MCP tool calls the new MediatR handler directly, as the other MCP tools do. Adding a
controller endpoint would leak an unused method into the auto-generated TypeScript client.

## Backend

```
Application/Features/MindMaps/
  Contracts/MindMapOperationDto.cs             flat operation DTO (class)
  Services/MindMapOperationApplier.cs          pure: current document + ops → target document
  Services/MindMapOperationException.cs        message + failing op index
  Services/MindMapOutlineRenderer.cs           pure: document + map metadata → outline text
  UseCases/ApplyMindMapOperations/             Request / Handler / Response
API/MCP/Tools/MindMapMcpTools.cs               three thin tool wrappers
```

`MindMapOperationApplier` and `MindMapOutlineRenderer` are pure functions over `MindMapDocument`
— no repository, no I/O. They hold everything worth testing and keep the handler and the tool
class thin. Both are registered in `MindMapsModule.cs` alongside the existing services.

### Handler flow (`ApplyMindMapOperationsHandler`)

1. `GetByIdAsync` → null → `ErrorCodes.ResourceNotFound`.
2. `Status == Updating` → `ErrorCodes.MindMapUpdateInProgress`.
3. `revision != map.UpdatedAt.Ticks` → `ErrorCodes.MindMapRevisionMismatch` (new).
4. Deserialize `CurrentJson`; run the applier. `MindMapOperationException` →
   `ErrorCodes.MindMapInvalidOperation` (new) with the op index in the error params.
5. `MindMapDocumentValidator.Validate` on the result → `ErrorCodes.MindMapInvalidDocument`.
   Defence in depth: the applier should not be able to produce a cycle or an orphan.
6. `MindMapLockService.ApplyUserEdit` with the caller's email from `ICurrentUserService`
   (missing email → `ErrorCodes.ValidationError`, as in the save handler).
7. Append the `MindMapVersion` snapshot **without setting `Id`** — EF marks a keyed child added to
   a tracked parent's navigation collection as Modified, which issues an `UPDATE` against a
   non-existent row.
8. Write `CurrentJson`, bump `UpdatedAt`, `SaveChangesAsync`.
9. Response (inherits `BaseResponse`): `DocumentJson`, `Revision`, `Name`, `Status`,
   `AssignedIds`, and the four counts — enough for the tool to render the new outline without a
   second round trip.

Validation lives in the applier, not in a FluentValidation validator: validators in this repo are
registered manually per module, and the operation rules are inherently document-relative
(does this node id exist, would this move create a cycle) rather than shape-level.

### Change to existing code

`MindMapLockService.ApplyUserEdit` returns a `UserEditResult { Document, AssignedIds }` instead of
a bare `MindMapDocument`, so the new-node id assignments can be reported back to the caller. One
existing call site (`SaveMindMapDocumentHandler`) and the existing `MindMapLockServiceTests` are
updated. The alternative — recovering new ids by node ordering — is an implicit coupling to
iteration order that would break silently.

### New error codes

`MindMapRevisionMismatch = 3404` and `MindMapInvalidOperation = 3405`. The 34XX Mind Maps bucket
already exists in `ErrorHandlingTests`, so no bucket change is needed, but both codes need Czech
strings in `frontend/src/i18n.ts` or the translation-coverage test fails.

The tool maps error codes to prefixed `McpException` messages so the model can act on them:
`[STALE]`, `[BUSY]`, `[INVALID]`, `[NOT FOUND]`, alongside the existing `[FORBIDDEN]` from
`EnsureFeatureAccess`.

## Testing

| Suite | Covers |
|---|---|
| `MindMapOperationApplierTests` | add under a real parent and under a `tempId`; multi-level subtree in one call; partial update; `""` clears notes/owner; title cannot be cleared; move rejects root / cycle / unknown parent; delete cascades and tombstones every descendant; delete rejects root; unknown node id reports the right op index; batch is not partially applied on failure |
| `MindMapOutlineRendererTests` | indentation and child order, 200-char note truncation, `(locked)` and `@owner` marks, meeting count shown only when non-zero, suppressed line present/absent, single-node map |
| `ApplyMindMapOperationsHandlerTests` | not found; `Updating`; stale revision; snapshot written with the next version number and `TriggerMeetingId = null`; lock applied to a content-edited node and not to a moved one; `UpdatedAt`/revision bumped; missing caller email |
| `MindMapMcpToolsTests` | Read vs Write feature gate per tool, parameter mapping, `format: "json"` passthrough, error-code → `McpException` mapping, JSON serialization via `McpJsonOptions.Default` |

Existing `MindMapLockServiceTests` are updated for the new return type. Target 80%+ coverage on
the new code; the two pure services carry most of it.

No frontend changes, no E2E (Playwright does not drive MCP), no database migration.

## Documentation

- `docs/integrations/mcp-server.md` — new "Mind Maps (3)" section describing the three tools, the
  revision/lock semantics, and the `Anela_MindMaps` Read/Write gates.
- `CLAUDE.md` — the MCP tool count is already stale (says 20, actual is 23); update it to 26.

## Out of scope

Creating, deleting, regenerating or restoring maps over MCP; attaching or detaching meetings;
dry-run previews; an operation cap; an MCP prompt or a shipped Claude Code skill; a true
database-level concurrency token; per-map access grants.
