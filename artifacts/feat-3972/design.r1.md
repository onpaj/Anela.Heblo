# Design: Fix silent data loss on malformed LLM JSON in meeting task extraction

## UX/UI Design

This change adds no new component, layout, color token, or interaction model. It extends two
already-existing pages with content variants of an amber warning idiom that already exists verbatim
in `MeetingTaskDetailPage.tsx` (the "neznámý uživatel" pill, lines ~579-583:
`text-amber-700 bg-amber-100 dark:text-amber-300 dark:bg-amber-900/30` + `AlertTriangle` icon).

**Trigger:** `transcript.tasksExtractionDegraded === true` on the transcript returned to either page.
Never true and false at once for the same transcript across the two pages — both read the same field
from the same entity.

### Detail page (`MeetingTaskDetailPage.tsx`) — full-width banner

Rendered directly under the header row that currently holds `TranscriptStatusBadge` (~line 293-294),
using the same amber palette as the existing `reimportError` block (lines 395-399) but promoted from
an inline message to a full-width banner so it reads as page-level state, not a transient error toast.

```
┌─────────────────────────────────────────────────────────────────────┐
│ Meeting Subject                                    [Status: Approved]│
│ 2026-08-28 14:30 · plaud-rec-123                                     │
│ Účastníci: Jana Nováková, Petr Svoboda                               │
├─────────────────────────────────────────────────────────────────────┤
│ ⚠  Extrakce úkolů může být neúplná — nepodařilo se zpracovat celou   │
│    odpověď AI. Zkontrolujte přepis ručně, nebo použijte tlačítko     │
│    "Reimportovat" níže.                                              │
└─────────────────────────────────────────────────────────────────────┘
```

- Placement: new block between the header `<div className="px-4 sm:px-6 lg:px-8 ...">` (ends ~line
  292) and whatever currently follows it — conditionally rendered on `transcript.tasksExtractionDegraded`.
- Style: reuse the existing amber tokens (`bg-amber-100`/`text-amber-700` light,
  `dark:bg-amber-900/30`/`dark:text-amber-300` dark), `AlertTriangle` icon (already imported at line 7),
  full width of the content column, not an inline pill.
- Content: states that task/participant extraction may be incomplete and names "Reimport" as the
  remedy (the existing button around line 364) — exact Czech copy is an implementation detail, not a
  design decision; match the terse, informal register already used elsewhere on this page (e.g.
  "neznámý uživatel", "Označit jako schváleno").
- Does not block or hide anything else on the page — tasks, participants, and all existing actions
  (approve, reimport, etc.) render exactly as they do today alongside it.
- No banner is rendered when the flag is false — no placeholder, no "extraction OK" affirmative state.

### List page (`MeetingTasksPage.tsx`) — row-level pill

Same amber pill idiom, shown inside the existing "Ulohy" `<td>` (line 191-198), immediately after the
existing task-count text, only for rows where `tasksExtractionDegraded` is true:

```
Ulohy
─────
5 (2 schvaleno)  ⚠
3                        ← flag not set: no pill, unchanged
```

- Reuses `AlertTriangle` at pill size (`w-3 h-3`), same amber tokens as the detail-page badge.
- No tooltip required by the spec, but if one already ships trivially via `title=`, a short label such
  as "extrakce může být neúplná" is fine — not a hard requirement.
- Row remains fully clickable/navigable to the detail page as before; the flag does not change
  filtering, sorting, or row eligibility for review/approval actions (per FR-3's explicit
  "informational only" constraint).

No wireframe is needed beyond the above — this is a content addition to two already-understood
layouts using an existing idiom, not a new UI surface.

## Component Design

### Backend

**`ClaudeMeetingTaskExtractor` (`Application/Features/MeetingTasks/Services/ClaudeMeetingTaskExtractor.cs`)**
— existing class, modified only inside its `catch (JsonException)` block:
- On catch: `LogError(ex, "...{RawResponse}", text)` where `text` is the post-`StripMarkdownCodeFence`,
  pre-deserialization string (FR-1). Structured property, not string-interpolated.
- Delegates recovery to the new `PartialExtractionParser.TrySalvage(text, _logger)` (FR-2) and builds
  the returned `MeetingExtractionResult` from its output.
- No other behavior change; the happy path (`JsonSerializer.Deserialize` succeeds) is untouched and
  must not invoke any new code (NFR-1).

**`PartialExtractionParser` (new, `Application/Features/MeetingTasks/Services/PartialExtractionParser.cs`)**
— internal static helper class, no DI, no interface (single caller, single implementation; matches the
file-local-static style already used for `StripMarkdownCodeFence`/`NormalizeParticipants` in the same
slice).

Entry point:
```csharp
internal static (List<ExtractedTask> Tasks, List<string> Participants, bool LocatedAnyArray)
    TrySalvage(string text, ILogger logger);
```

Internal primitives (unit-tested independently, in isolation from any Claude/chat-client mocking):
- `FindTopLevelArrayBody(text, propertyName) -> string?` — locates the `"tasks": [ ... ]` /
  `"participants": [ ... ]` array body via manual depth/quote/escape-aware character scanning (tracks
  `{`/`}`/`[`/`]` nesting depth and in-string/escape state; never itself requires the scanned bytes to
  be valid JSON). Returns `null` if the key or a balanced bracket pair cannot be located.
- `SplitTopLevelElements(arrayBody) -> IReadOnlyList<string>` — splits an array body into individual
  element substrings at depth-1 commas, same scanning discipline. Returns an empty list for a `null` or
  empty body.
- Per-element deserialization: each element substring is independently passed to the *same*
  `JsonSerializer.Deserialize<ExtractedTask>` / `Deserialize<string>` used on the happy path — no
  bespoke per-field parser. A substring that still contains the malformed byte throws there; that
  exception is caught, logged (`LogWarning`, with the element's 0-based index and raw substring), and
  the element is skipped. Order of successfully-parsed elements is preserved.
- If neither array body can be located at all, `LocatedAnyArray` is `false` and the caller falls back
  to `MeetingExtractionResult([], [], Degraded: true)` plus the FR-1 `LogError` (already emitted by the
  caller before invoking `TrySalvage`).

**`IMeetingTaskExtractor` / `MeetingExtractionResult` (`Services/IMeetingTaskExtractor.cs`)** — existing
record, gains one field with a default so it stays source-compatible:
```csharp
public record MeetingExtractionResult(
    List<ExtractedTask> Tasks,
    List<string> Participants,
    bool Degraded = false);
```
Stays a `record` — internal application-layer type, not NSwag-serialized, single implementation/caller
path; the DTOs-must-be-classes rule does not apply here.

**`IngestPlaudRecordingHandler`, `ReimportMeetingTranscriptHandler`** — one line each, next to the
existing `Participants = extraction.Participants` assignment:
```csharp
entity.TasksExtractionDegraded = extraction.Degraded;       // Ingest — new entity
transcript.TasksExtractionDegraded = extraction.Degraded;   // Reimport — unconditional overwrite,
                                                              // never OR'd with the prior value, so a
                                                              // clean reimport clears a stale flag.
```

**`GetTranscriptDetailHandler`, `GetTranscriptListHandler`** — one line each, mapping
`entity.TasksExtractionDegraded` onto `MeetingTranscriptDto.TasksExtractionDegraded`.

### Frontend

**`useMeetingTasks.ts`** — no new component; the hand-written `MeetingTranscriptDto` interface
(line 28) gains one field:
```typescript
export interface MeetingTranscriptDto {
  // ...existing fields...
  tasksExtractionDegraded: boolean;
}
```
This interface is populated by the hook's own `fetchJson` calls against the raw REST endpoint, not by
importing from the generated `api-client.ts` — the generated client is regenerated for consistency but
is not itself consumed by this hook (pre-existing bypass; not addressed by this change).

**`MeetingTaskDetailPage.tsx`** — no new component; a conditionally-rendered `<div>` block reusing the
existing amber classes and `AlertTriangle` icon, inserted near the header/`TranscriptStatusBadge` row.

**`MeetingTasksPage.tsx`** — no new component; a conditionally-rendered `<span>` pill reusing the same
classes, inserted inside the existing "Ulohy" `<td>`.

## Data Schemas

### `MeetingExtractionResult` (application-layer record, C#)
```csharp
public record MeetingExtractionResult(
    List<ExtractedTask> Tasks,
    List<string> Participants,
    bool Degraded = false);
```
- `Degraded = true` whenever one or more tasks/participants were dropped due to a parse error —
  whether via partial salvage (some elements recovered) or full fallback (no array locatable at all).
- `Degraded = false` only when the top-level `JsonSerializer.Deserialize<ExtractionPayload>` succeeded
  outright; nothing is ever dropped in that case.

### `MeetingTranscript` (domain entity, EF-mapped)
```csharp
public bool TasksExtractionDegraded { get; set; }
```
- Persisted column, `nullable: false`, `defaultValue: false` (existing rows default to not-degraded;
  no backfill).
- Set from `MeetingExtractionResult.Degraded` on both ingest and reimport; reimport unconditionally
  overwrites (never OR-merges) so the flag reflects only the latest extraction attempt.

### `MeetingTranscriptDto` (Contracts class, OpenAPI-serialized)
```csharp
public bool TasksExtractionDegraded { get; set; }
```
- Added as a plain `bool` property alongside the existing ones on this already-`class` DTO — no record
  conversion, no generation risk.
- Surfaced on both the transcript-detail and transcript-list read paths consumed by
  `MeetingTaskDetailPage.tsx` and `MeetingTasksPage.tsx`.
- Inherits the existing `IMeetingAccessGuard`-based access control unchanged; no new auth surface.

### Frontend hand-written interface (`useMeetingTasks.ts`)
```typescript
export interface MeetingTranscriptDto {
  // ...existing fields (id, subject, summary, status, tasks, participants, ...)...
  tasksExtractionDegraded: boolean;
}
```
- Manually kept in sync with the backend DTO (pre-existing pattern for this file — not a generated
  type). Both `TranscriptListResponse.items[]` and `TranscriptDetailResponse.transcript` carry this
  field via the shared interface.

### Logging payloads (not persisted, structured log properties only)
- FR-1 failure log: `LogError(ex, "<message>", text)` — `text` = full raw response after markdown-fence
  stripping, before deserialization; no truncation.
- FR-2 per-element skip log: `LogWarning(ex, "<message>", index, rawElementSubstring)` — one entry per
  dropped task/participant element, structured `Index` and raw element text.

### Migration
- New manual EF Core migration (e.g. `AddTasksExtractionDegraded`) adding a single
  `bool` column to the `MeetingTranscripts` table, `nullable: false, defaultValue: false`, following
  the shape of prior single-bool-column migrations in this project (e.g. `AddMeetingParticipants`,
  `AddInvoiceAcquiredToPurchaseOrder`). Applied manually to each environment per project convention —
  not part of automated deployment.
