---
process: job-mindmap-update
kind: job
module: mind-maps
summary: Background Hangfire job that feeds each newly attached meeting transcript to Claude and merges the reply into the mind map under a deterministic lock guard, snapshotting a version before every change.
owns:
  - backend/src/Anela.Heblo.Application/Features/MindMaps/Infrastructure/Jobs/**
  - backend/src/Anela.Heblo.Application/Features/MindMaps/Services/ClaudeMindMapUpdater.cs
  - backend/src/Anela.Heblo.Application/Features/MindMaps/Services/StubMindMapUpdater.cs
  - backend/src/Anela.Heblo.Application/Features/MindMaps/Services/IMindMapUpdater.cs
  - backend/src/Anela.Heblo.Application/Features/MindMaps/Services/MindMapGuard*.cs
  - backend/src/Anela.Heblo.Application/Features/MindMaps/Services/MindMapUpdateException.cs
  - backend/src/Anela.Heblo.Application/Features/MindMaps/Prompts/**
  - backend/src/Anela.Heblo.Application/Features/MindMaps/MindMapsOptions.cs
  - backend/src/Anela.Heblo.Application/Features/MindMaps/MindMapsConstants.cs
  - backend/src/Anela.Heblo.Application/Features/MindMaps/UseCases/AttachMeeting/**
  - backend/src/Anela.Heblo.Application/Features/MindMaps/UseCases/RegenerateMindMap/**
verified_at: "5e993f9e2"
related: []
---

# Mind map update from meetings (Claude)

## Purpose
Keeps a "living" project mind map (Myšlenkové mapy) up to date with what was said in meetings.
A user attaches a recorded meeting (Porada) to a map; this job sends the meeting's summary and
full transcript together with the current map to Claude, which returns the next version of the
map: new projects/workstreams as nodes, dated status notes on existing nodes, statuses
(`active` / `done` / `blocked` / `idea`), owners. Finished work moves under „Hotovo",
postponed work under „Odloženo"; Claude never deletes anything. Map content is written in Czech.

Before Claude's reply is stored, a deterministic guard (`MindMapGuard`) enforces what the model
must not do — so nodes a person edited by hand (locked) keep their title, notes and owner, nodes a
person deleted are not re-created, and layout (position, collapsed) is preserved.

The result is shown on `/automation/mind-maps/{id}`; the page polls the detail endpoint every 3 s
while the map is `Updating`.

## Trigger
No recurring job and no cron. `MindMapUpdateJob.RunAsync(mindMapId)` is **enqueued** in Hangfire
(default queue, no fixed job id) by two user actions:

| Action | Endpoint (permission `anela.mind_maps.write`) | Handler |
|---|---|---|
| "Připojit poradu" (attach meeting) in the "Porady" side-panel tab | `POST /api/mind-maps/{id}/meetings` | `AttachMeetingHandler` |
| "Regenerovat" button on the map page (shown only when the map is `Failed` or has a pending meeting) | `POST /api/mind-maps/{id}/regenerate` | `RegenerateMindMapHandler` |

Job attributes: `[DisableConcurrentExecution("mindmap-update:{0}", 600)]` (one run per map at a
time, 600 s lock wait) and `[AutomaticRetry(Attempts = 10)]`.

Map status (`MindMaps.Status`): `Idle` → `Updating` (set by the handler before enqueue, re-asserted
by the job when it has pending work) → `Idle` on success, or `Failed` with `LastError`.

## Data flow
1. **Attach** (`AttachMeetingHandler`): load the map and the `MeetingTranscript`; refuse with
   `ResourceNotFound` if the meeting does not exist **or the current user may not see it**
   (`IMeetingAccessGuard`: meetings managers see all; others Public, or Restricted with a grant);
   refuse `MindMapMeetingAlreadyAttached` (3502) if already linked. Insert a
   `public."MindMapMeetings"` row (`AttachedAt` = now, `ProcessedAt` = null = pending), set
   status `Updating`, save, enqueue the job. A concurrent duplicate attach is caught on the unique
   index `UX_MindMapMeetings_MindMapId_MeetingTranscriptId` and reported as 3502.
2. **Regenerate** (`RegenerateMindMapHandler`): refused with `MindMapUpdateInProgress` (3501)
   while `Updating`. If no meeting is pending, just set `Idle`, clear `LastError`, and stop (no job).
   Otherwise set `Updating`, save, enqueue. Regenerate does **not** re-process already processed
   meetings — it only resumes pending ones (typically after a failure).
3. If the enqueue itself throws, both handlers revert the status (Regenerate also restores
   `LastError`) and return `InternalServerError`; the attached meeting row stays and is picked up
   by the next Regenerate.
4. **Job** (`MindMapUpdateJob`): load the map with meetings, transcripts and all versions
   (`GetForUpdateAsync`). Pending = meetings with `ProcessedAt IS NULL` and a loaded transcript,
   ordered by `MeetingTranscript.PlaudCreatedAt` (meeting date, oldest first). For **each**
   pending meeting, one at a time:
   1. deserialize `MindMaps.CurrentJson`;
   2. `IMindMapUpdater.UpdateAsync` → Claude (see Logic);
   3. `MindMapGuard.ApplyLlmUpdate` → guarded document;
   4. insert a `public."MindMapVersions"` row holding the **pre-update** JSON, `VersionNumber` =
      max + 1, `TriggerMeetingId` = this meeting;
   5. write the guarded JSON to `MindMaps.CurrentJson`, set `MindMapMeetings.ProcessedAt` = now,
      save (one `SaveChanges` per meeting — earlier meetings stay processed if a later one fails).
5. All done → status `Idle`, `LastError` null, `UpdatedAt` now.

External call: Anthropic Messages API `https://api.anthropic.com/v1/messages` via the keyed
`IChatClient` `"mindmap-updater"` (`AnthropicAdapterServiceCollectionExtensions`).

## Logic & formulas
**Prompt.** System prompt = embedded resource `Prompts/mindmap-update-skill.md` (rules: durable
things are nodes, current events are dated `[yyyy-MM-dd]` note lines, ≤ 10 dated lines per node,
≤ 9 first-level branches besides „Hotovo"/„Odloženo", depth 2–4, owner only from participants or
named in the meeting, never invent deadlines, mark uncertainty `(ověřit)`/`(riziko)`/`(odhad)`,
new nodes get ids `new-1`, `new-2`…). User message = current map as JSON (only `id`, `parentId`,
`title`, `notes`, `status`, `owner`, `locked`, `sourceMeetingIds`; plus `doNotRecreate` = titles of
user-deleted nodes) + meeting subject and date + `Participants` (omitted when empty) + `Summary` +
full `RawTranscript`.

**Updater retries** (`ClaudeMindMapUpdater`, `MaxAttempts = 2`): a markdown code fence is
stripped, the reply is deserialized and run through `MindMapDocumentValidator` (non-empty nodes,
unique non-empty ids, non-empty titles, known statuses, exactly one root equal to `rootNodeId`,
every parent exists, no cycles). Invalid → one more call with a corrective message; still invalid →
`MindMapUpdateException`. HTTP 429/529 are retried separately by the adapter's Polly pipeline
(3 retries, exponential from 2 s, honours `Retry-After`).

**Guard** (`MindMapGuard`, works on clones):
- Root id changed → hard failure. Empty or duplicate node ids, or a null title on a non-locked
  node → hard failure.
- New nodes whose title (trimmed, case-insensitive) matches a suppressed (user-deleted) title are
  removed; their children are re-parented to the removed node's parent.
- Locked nodes (`lockedBy` set): title, notes and owner are restored from the previous version
  (status and parent may change). If Claude dropped a locked node it is re-inserted under its
  nearest surviving ancestor (root as fallback).
- Nodes with ids unknown to the previous version get a fresh server id (`Guid` "N") and this
  meeting's id added to `sourceMeetingIds`; child `parentId`s are remapped.
- Existing nodes keep `position`, `collapsed`, `lockedBy` from the previous version, and
  `sourceMeetingIds` = previous ∪ Claude's (provenance can be added, never dropped). New nodes get
  no position, not collapsed, not locked.
- `schemaVersion` and `suppressedNodes` are copied from the previous version; the result is
  validated again — invalid → hard failure.

**Failure handling.**
- Any exception while processing a meeting (Claude error, invalid reply, guard failure, DB error)
  → `MarkFailedAsync`: status `Failed`, `LastError` = exception message, written by a
  change-tracker-free `ExecuteUpdate` (`SetFailedAsync`) so a poisoned DbContext cannot block it.
  The job then **returns normally** — Hangfire records Succeeded and does not retry. The failed
  meeting and all later ones stay pending until a user clicks Regenerate.
- `OperationCanceledException` (deploy/restart) → `Failed` with "Job cancelled.", then rethrown so
  Hangfire retries (up to 10 attempts); a retry resumes at the first unprocessed meeting and
  re-sets `Updating`.

**Stub updater.** With `MindMaps:UseStubUpdater = true` no Claude call is made:
`StubMindMapUpdater` just adds one node „Porada: &lt;subject&gt;" (status `idea`) under the root.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `MindMaps:UseStubUpdater` | `false` (`appsettings.json`); `true` in `appsettings.Staging.json` | Replace Claude with the deterministic stub. |
| `MindMaps:UpdaterMaxOutputTokens` | `16384` (code default, range 1024–64000) | `max_tokens` per Claude call; the whole map must fit in one reply. |
| `KnowledgeBase:ChatModel` | `claude-sonnet-4-6` | Model used — the mind map client shares `AnthropicOptions.Model` with the knowledge-base chat. |
| `Anthropic:ApiKey` | `""` (secret, Key Vault) | Empty → every update fails with "Anthropic:ApiKey is not configured.". |
| `Anthropic:HttpTimeoutSeconds` | `180` | HTTP timeout of the `"Anthropic"` client used per call. |
| `Hangfire:WorkerCount` | `1` (`HangfireOptions` default, not set in repo json) | Shared worker pool; see quirks. |

## Runtime facts
- Production Hangfire runs with a single worker (`WorkerCount = 1`), so any long job delays all
  others — memory note `gotcha_hangfire_single_worker_starves_print`, 2026-09-29.

## Known quirks
- **Save-vs-job race has no concurrency token.** Manual save and restore are only blocked by a
  `Status == Updating` check; a save that slips past it silently overwrites (or is overwritten by)
  the job's update — last writer wins, no version row records it. Fix idea: an Npgsql `xmin`
  concurrency token on `MindMap` (no migration). Deferred because tests run on EF InMemory
  (memory note `gotcha_mindmap_concurrency_no_token`, 2026-08-10).
- **Corrective retry does not show Claude its bad reply.** `AnthropicChatClient` sends only the
  system message and User-role messages, so the Assistant message the updater appends before its
  "Your previous reply was not valid" follow-up is dropped; the second attempt sees two consecutive
  user messages and never its own output.
- **HTTP timeout is reported as "Job cancelled." and retried up to 10×.** A Claude call exceeding
  `Anthropic:HttpTimeoutSeconds` throws `TaskCanceledException`, which is an
  `OperationCanceledException`; the job treats it as a deploy cancellation, sets `Failed` /
  "Job cancelled." and rethrows, so Hangfire re-runs the whole (expensive) Claude call up to 10
  times. Other errors (invalid JSON, 4xx/5xx) fail once and wait for Regenerate.
- **Large maps can outgrow one reply.** The full map is rewritten on every meeting; once its JSON
  exceeds `UpdaterMaxOutputTokens` the reply is truncated, fails validation twice and the map goes
  `Failed`.
- **One worker, long job.** Each meeting costs 1–2 Claude calls of up to 180 s each; with
  `WorkerCount = 1` an update of several meetings blocks every other Hangfire job (e.g. picking-list
  prints) meanwhile.
- **Detach during an update.** Neither `DetachMeetingHandler` nor the "Odpojit" button (not
  disabled while `Updating`) blocks it; detaching the meeting the job is processing deletes the row
  the job then marks processed, so its save fails and the map goes `Failed` (inferred from code,
  not observed).
- **Meeting privacy leaks into the map.** Attach checks that the *attaching* user may see the
  meeting, but the resulting map (content derived from the full transcript, meeting subjects in
  the version list) is visible to everyone with `anela.mind_maps.read`.
- **Order is by meeting date, not attach date.** Attaching an older meeting after newer ones were
  processed applies it last, on top of the newer state; its dated note lines still carry the older
  date.
- Already processed meetings are never re-processed — Regenerate is "resume pending", not
  "rebuild from scratch"; to undo a bad update, restore the version created before it.
- Prompt is the source of truth for map semantics; changing `mindmap-update-skill.md` changes
  behaviour for every map from the next meeting on, with no versioning of the prompt itself.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/MindMaps/Infrastructure/Jobs/MindMapUpdateJob.cs` — loop, status, versioning, failure handling
- `backend/src/Anela.Heblo.Application/Features/MindMaps/Services/ClaudeMindMapUpdater.cs` — prompt assembly, retry on invalid reply
- `backend/src/Anela.Heblo.Application/Features/MindMaps/Prompts/mindmap-update-skill.md` — Claude's instructions (map rules)
- `backend/src/Anela.Heblo.Application/Features/MindMaps/Services/MindMapGuard.cs` — lock / suppression / id / metadata enforcement
- `backend/src/Anela.Heblo.Application/Features/MindMaps/UseCases/AttachMeeting/AttachMeetingHandler.cs` — attach + enqueue
- `backend/src/Anela.Heblo.Application/Features/MindMaps/UseCases/RegenerateMindMap/RegenerateMindMapHandler.cs` — resume pending
- `backend/src/Adapters/Anela.Heblo.Adapters.Anthropic/AnthropicAdapterServiceCollectionExtensions.cs` — keyed `"mindmap-updater"` chat client
