# Architecture Review: Fix silent data loss on malformed LLM JSON in meeting task extraction

## Skip Design: true

No new screens, layouts, or visual components are introduced. `MeetingTaskDetailPage.tsx` already
has an established inline warning pattern to extend verbatim: the "neznámý uživatel" badge
(`AlertTriangle` icon + `text-amber-700 bg-amber-100 dark:text-amber-300 dark:bg-amber-900/30`,
lines ~579-583) and the `reimportError` inline message block (lines 395-399) right below the header
row where `TranscriptStatusBadge` lives. FR-3's banner is the same amber alert idiom promoted to a
full-width block instead of an inline pill, referencing the existing "Reimport" button by name — no
new component library, color token, or interaction model is needed. `MeetingTasksPage.tsx`'s row-level
indicator is the same amber pill dropped into the existing "Ulohy" `<td>`. This is a content/state
addition to two already-understood layouts, not a design decision — a designer pass would add process
overhead without changing the outcome. (If a reviewer disagrees, the risk is low: worst case is a
follow-up cosmetic tweak, not a rework.)

## Architectural Fit Assessment

This is a textbook bug-fix-in-place: no new module, no new endpoint, no new module boundary crossed.
All three FRs land inside the existing `MeetingTasks` vertical slice and touch files that already
exist:

- **FR-1** is a one-line change to an existing `catch (JsonException)` block in
  `ClaudeMeetingTaskExtractor.cs` — pure logging, no structural change.
- **FR-2** adds a private fallback code path inside the same class (or a small sibling helper in the
  same `Services/` folder) that only activates when the existing `catch` block is hit. It changes the
  *shape* of `MeetingExtractionResult` (adds a `bool Degraded`), which is an internal
  `Services/IMeetingTaskExtractor.cs` type — not an API contract, not NSwag-serialized, currently a
  `record`, and has exactly one implementation (`ClaudeMeetingTaskExtractor`) and one production caller
  path (`IngestPlaudRecordingHandler`, `ReimportMeetingTranscriptHandler`). The **DTOs-must-be-classes
  rule does not apply here** — that rule targets `Contracts/` types crossing the OpenAPI boundary, and
  `MeetingExtractionResult`/`ExtractedTask` are already records today with no generation issue, so
  they should **stay records**.
- **FR-3** adds one column to `MeetingTranscript` (already a plain class, entities are unaffected by
  the DTO-record rule), threads it through the two handlers that already construct/mutate this entity
  from `MeetingExtractionResult`, exposes it on `MeetingTranscriptDto` (**this one is a `Contracts/`
  class — the new `TasksExtractionDegraded` property must be a plain `bool` on the existing class,
  which it already is by construction; no record risk here since the file is already a `class`**), and
  surfaces it in two already-existing pages via an already-existing visual idiom.

**One integration point needs correcting before implementation starts** (see Specification Amendments
below): the spec assumes the frontend "picks up" the new field via "the next generated-client build."
That is not true for this module — `useMeetingTasks.ts` hand-rolls its own `MeetingTranscriptDto`
TypeScript interface and calls the backend via a raw `fetchJson` helper, bypassing the generated
`api-client.ts` entirely (there's a stale `// TODO: migrate to generated client...` comment at the top
of the file, and the generated client in fact already has `meetingTasks_*` methods and a generated
`MeetingTranscriptDto` class — the migration just never happened). The new field will not reach the UI
without a manual edit to that hand-written interface.

**A second, more consequential issue** with the spec's proposed FR-2 mechanism: `JsonDocument.Parse`
(and `JsonSerializer.Deserialize`) require the **entire input to be well-formed JSON** to parse at
all — there is no "permissive" `JsonDocumentOptions` that tolerates an invalid byte/token anywhere in
the document and still returns the elements around it. Calling `JsonDocument.Parse` on the *same*
malformed `text` that just failed `JsonSerializer.Deserialize` will throw the **identical**
`JsonException` at the **identical** position, before any array element is reachable. The literal
technique named in FR-2's acceptance criteria therefore cannot achieve element-level salvage as
written. See Decision 2 below for the design that actually achieves the stated goal.

## Proposed Architecture

### Component Overview

```
IngestPlaudRecordingHandler ──┐
                               ├──> IMeetingTaskExtractor.ExtractAsync(summary, transcript)
ReimportMeetingTranscriptHandler ┘         │
                                            ▼
                                 ClaudeMeetingTaskExtractor
                                   ├─ IChatClient.GetResponseAsync (Claude call, unchanged)
                                   ├─ StripMarkdownCodeFence (unchanged)
                                   ├─ try: JsonSerializer.Deserialize<ExtractionPayload>(text)
                                   │     └─ success → MeetingExtractionResult(tasks, participants, Degraded:false)
                                   └─ catch (JsonException ex):
                                         ├─ LogError(ex, "...{RawResponse}", text)         [FR-1]
                                         └─ PartialExtractionParser.TrySalvage(text)       [FR-2, new]
                                               ├─ located tasks/participants arrays?
                                               │    ├─ yes → per-element deserialize,
                                               │    │        LogWarning per dropped element,
                                               │    │        MeetingExtractionResult(salvaged, Degraded:true)
                                               │    └─ no  → LogError(text),
                                               │             MeetingExtractionResult([], [], Degraded:true)
                                            │
                                            ▼
                                 MeetingExtractionResult { Tasks, Participants, Degraded }
                                            │
                     ┌──────────────────────┴───────────────────────┐
                     ▼                                               ▼
     IngestPlaudRecordingHandler                     ReimportMeetingTranscriptHandler
       entity.TasksExtractionDegraded                  transcript.TasksExtractionDegraded
         = extraction.Degraded                           = extraction.Degraded   [always overwritten,
                                                                                    clears stale flag]
                     │                                               │
                     └──────────────────────┬───────────────────────┘
                                            ▼
                              MeetingTranscript (entity, +1 column)
                                            │
                          GetTranscriptDetailHandler / GetTranscriptListHandler
                                            │
                              MeetingTranscriptDto (+tasksExtractionDegraded)
                                            │
                                   api-client.ts (regenerated)
                                            │
                    useMeetingTasks.ts  (hand-written interface — MANUAL edit required)
                                            │
                     ┌──────────────────────┴───────────────────────┐
                     ▼                                               ▼
        MeetingTaskDetailPage.tsx                         MeetingTasksPage.tsx
       (warning banner near TranscriptStatusBadge)        (amber pill in "Ulohy" column)
```

### Key Design Decisions

#### Decision 1: Where the fallback parser lives

**Options considered:**
- Inline the salvage logic directly inside `ClaudeMeetingTaskExtractor.ExtractAsync`'s catch block.
- Extract a separate internal static helper class in the same `Services/` folder.

**Chosen approach:** A new internal static class, `Services/PartialExtractionParser.cs`, with a single
entry point `TrySalvage(string text, ILogger logger) -> (List<ExtractedTask>, List<string>, bool
locatedAnyArray)`, invoked from the existing `catch (JsonException)` block.

**Rationale:** The salvage logic (bracket/string-depth scanning, per-element try/catch, index-tracked
warnings) is non-trivial and deserves focused unit tests independent of the Claude chat-client mocking
already used in `ClaudeMeetingTaskExtractorTests`. Keeping it a plain static class (no DI, no
interface) matches the file-scoped-helper style already used in the same file (`StripMarkdownCodeFence`,
`NormalizeParticipants` are private statics) while still being unit-testable by giving it its own test
file, `PartialExtractionParserTests.cs`, that feeds raw malformed JSON strings directly — no chat-client
mocking needed for the FR-2 edge cases. This keeps `ClaudeMeetingTaskExtractor` as thin orchestration
(consistent with `development_guidelines.md`'s "Services/" placement for business logic) while avoiding
a speculative new interface for a single-implementation, single-caller concern.

#### Decision 2: Salvage mechanism — how to actually recover valid array elements from a malformed document

**Options considered:**
1. **`JsonDocument.Parse` in a "permissive" mode, per the spec's literal wording.** Rejected: no such
   mode exists in `System.Text.Json`. `JsonDocumentOptions` only relaxes trailing commas and comment
   handling — it does not tolerate an invalid UTF-8 byte or unescaped control character inside a string
   value anywhere in the document. Since the telemetry shows the malformed byte is *inside* a string
   value (e.g. an unescaped `0xE2` lead byte or stray `"`), `JsonDocument.Parse(text)` throws at the
   exact same position as `JsonSerializer.Deserialize(text)` did, before returning any elements. This
   option cannot deliver "keep the good tasks, drop the one bad task" — it fails the same as today.
2. **Regex-based extraction of `{...}` blocks.** Rejected: regex cannot reliably track nested braces,
   quoted strings containing braces (a task `description` can easily contain `{` from a code snippet
   pasted into the transcript), or backslash-escaped quotes. Fragile and hard to reason about.
3. **A custom depth-aware raw-text scanner** that locates the `"tasks": [ ... ]` and
   `"participants": [ ... ]` array bodies by scanning `text` character-by-character (tracking `{`/`}`/
   `[`/`]` nesting depth and in-string/escape state, *not* calling into any JSON parser for this step),
   splits each array body into top-level element substrings at depth-1 commas, and then attempts
   `JsonSerializer.Deserialize<ExtractedTask>` / `JsonSerializer.Deserialize<string>` **independently
   per substring** — catching and logging (`LogWarning`, with index + raw substring) only the
   substring(s) that individually fail.

**Chosen approach:** Option 3.

**Rationale:** This is the only approach that satisfies FR-2's actual intent (isolate one bad element,
keep the rest) given that the malformation lives inside a string value, not at a document boundary.
Locating array boundaries via manual bracket/quote-depth tracking works even when the *content* between
brackets contains invalid bytes, because that tracking only inspects structural characters (`{ } [ ] "
\`) — it never needs the byte sequence between them to itself be valid UTF-8/JSON to find where one
element ends and the next begins. Once isolated, each element substring is still handed to the same
`JsonSerializer.Deserialize<ExtractedTask>` used on the happy path — no bespoke per-field parsing is
invented, keeping the object-shape contract (`ExtractedTask`) in exactly one place. A substring that
still contains the bad byte will still throw on that call — that is by design: it is the one dropped
element, logged and skipped, exactly per FR-2's acceptance criteria (a).

**Concrete algorithm shape** (for the developer, not literal code):
```
TrySalvage(text):
    tasksBody   = FindTopLevelArrayBody(text, "tasks")          // null if key/brackets not found
    participantsBody = FindTopLevelArrayBody(text, "participants")
    if tasksBody is null and participantsBody is null:
        return (tasks: [], participants: [], locatedAnyArray: false)   // → FR-2 (b): full fallback

    taskSubstrings = SplitTopLevelElements(tasksBody)    // depth/quote-aware comma split; [] if body null
    participantSubstrings = SplitTopLevelElements(participantsBody)

    tasks = []
    for (i, sub) in taskSubstrings:
        try: tasks.Add(JsonSerializer.Deserialize<ExtractedTask>(sub, JsonOptions))
        catch JsonException ex: logger.LogWarning(ex, "Dropping malformed task at index {Index}: {RawElement}", i, sub)

    participants = []  // same pattern, then NormalizeParticipants(...)

    return (tasks, participants, locatedAnyArray: true)
```
`FindTopLevelArrayBody` and `SplitTopLevelElements` are the two primitives to unit test hardest —
escaped-quote handling (`\"`), nested braces inside a `description` string, and an unterminated
trailing element at EOF (truncation, still possible even though telemetry currently shows mid-array
failures) are the edge cases the acceptance-criteria tests in FR-2 must cover, plus at least one test
per primitive in isolation.

#### Decision 3: `Degraded` propagation on reimport — always overwrite, never OR-merge

**Options considered:**
- OR the new `Degraded` value with the transcript's existing `TasksExtractionDegraded` (sticky once
  set).
- Unconditionally overwrite `TasksExtractionDegraded` with the latest `extraction.Degraded` on every
  ingest/reimport.

**Chosen approach:** Unconditional overwrite.

**Rationale:** FR-3's acceptance criteria are explicit: "a reimport that now succeeds cleanly must
clear a previously-set flag." This matches the existing reimport pattern in
`ReimportMeetingTranscriptHandler`, which already unconditionally replaces `Participants` and the
pending-task set from the latest `extraction` on every call — no other field in that handler is
merged/stickied across reimports. Overwriting is one line in each handler
(`entity.TasksExtractionDegraded = extraction.Degraded;` /
`transcript.TasksExtractionDegraded = extraction.Degraded;`) placed next to the existing
`Participants = extraction.Participants` assignment.

## Implementation Guidance

### Directory / Module Structure

All new code stays inside the existing `MeetingTasks` slice — no new folders beyond one new file:

```
backend/src/Anela.Heblo.Application/Features/MeetingTasks/
├── Services/
│   ├── ClaudeMeetingTaskExtractor.cs      # MODIFY: FR-1 log line + catch-block delegates to fallback
│   ├── IMeetingTaskExtractor.cs           # MODIFY: MeetingExtractionResult gains `Degraded` (record, default false)
│   └── PartialExtractionParser.cs         # NEW: FR-2 salvage primitives (static, no DI)
├── UseCases/
│   ├── IngestPlaudRecording/IngestPlaudRecordingHandler.cs     # MODIFY: 1 line, set TasksExtractionDegraded
│   └── ReimportMeetingTranscript/ReimportMeetingTranscriptHandler.cs  # MODIFY: 1 line, same
└── Contracts/
    └── MeetingTranscriptDto.cs            # MODIFY: + `public bool TasksExtractionDegraded { get; set; }`

backend/src/Anela.Heblo.Domain/Features/MeetingTasks/
└── MeetingTranscript.cs                   # MODIFY: + `public bool TasksExtractionDegraded { get; set; }`

backend/src/Anela.Heblo.Persistence/
├── MeetingTasks/MeetingTranscriptConfiguration.cs   # MODIFY: + builder.Property(x => x.TasksExtractionDegraded).IsRequired()
└── Migrations/{timestamp}_AddTasksExtractionDegraded.cs   # NEW: manual migration, see Prerequisites

backend/src/Anela.Heblo.Application/Features/MeetingTasks/UseCases/
├── GetTranscriptDetail/GetTranscriptDetailHandler.cs   # MODIFY: 1 line, map dto.TasksExtractionDegraded
└── GetTranscriptList/GetTranscriptListHandler.cs       # MODIFY: 1 line, same

frontend/src/api/hooks/useMeetingTasks.ts               # MODIFY: MeetingTranscriptDto interface + tasksExtractionDegraded: boolean
frontend/src/components/pages/automation/
├── MeetingTaskDetailPage.tsx              # MODIFY: warning banner near TranscriptStatusBadge (~line 293)
└── MeetingTasksPage.tsx                   # MODIFY: amber pill in "Ulohy" <td> (~line 191)

backend/test/Anela.Heblo.Tests/Features/MeetingTasks/
├── ClaudeMeetingTaskExtractorTests.cs      # MODIFY: add FR-1/FR-2 cases
├── PartialExtractionParserTests.cs         # NEW: primitive-level unit tests
├── IngestPlaudRecordingHandlerTests.cs     # MODIFY: assert TasksExtractionDegraded propagates
└── ReimportMeetingTranscriptHandlerTests.cs # MODIFY: assert overwrite-not-OR semantics (both directions)
```

No new `Module.cs` registration is needed — `PartialExtractionParser` is a static helper, not a DI
service.

### Interfaces and Contracts

```csharp
// Services/IMeetingTaskExtractor.cs — internal application-layer type, NOT an OpenAPI contract.
// Stays a record: no NSwag/generation exposure, only one implementation, source-compatible via
// the default parameter (per spec's Data Model section).
public record MeetingExtractionResult(
    List<ExtractedTask> Tasks,
    List<string> Participants,
    bool Degraded = false);
```

```csharp
// Domain/Features/MeetingTasks/MeetingTranscript.cs — entity, plain class (already is).
public bool TasksExtractionDegraded { get; set; }
```

```csharp
// Application/Features/MeetingTasks/Contracts/MeetingTranscriptDto.cs
// ⚠ This file IS an OpenAPI contract type crossing to the frontend via NSwag — it MUST stay a
// class (it already is). Add the new field as a plain bool property, same style as every other
// property on this DTO — do not convert this file to a record:
public bool TasksExtractionDegraded { get; set; }
```

**Frontend — the field will NOT auto-propagate to the UI from client generation alone.** Two edits are
required, not one:
1. `frontend/src/api/generated/api-client.ts` regenerates automatically (`MeetingTranscriptDto` class
   there already exists and will pick up the new field) — no manual work.
2. `frontend/src/api/hooks/useMeetingTasks.ts` defines its **own** hand-written
   `export interface MeetingTranscriptDto { ... }` (line 28) and fetches via a local `fetchJson`
   helper, never importing from `api-client.ts`. Add
   `tasksExtractionDegraded: boolean;` to that interface by hand. (Do not use this task to migrate the
   whole hook to the generated client — that is a larger, unrelated change; see Specification
   Amendments.)

### Data Flow

**Ingest (happy path, unchanged):** Plaud → `IPlaudClient` → `ClaudeMeetingTaskExtractor.ExtractAsync`
→ `JsonSerializer.Deserialize` succeeds → `MeetingExtractionResult(tasks, participants, Degraded:
false)` → `IngestPlaudRecordingHandler` maps `Degraded` straight onto `entity.TasksExtractionDegraded`
→ persisted → `GetTranscriptDetailHandler`/`GetTranscriptListHandler` map it onto the DTO → API →
regenerated client → **manually updated** `useMeetingTasks.ts` interface → page renders no banner
(`tasksExtractionDegraded === false`).

**Ingest (top-level parse failure, partial salvage):** `JsonSerializer.Deserialize` throws
`JsonException` → FR-1 logs `ex` + full `text` as a structured property → `PartialExtractionParser
.TrySalvage(text, logger)` locates the `tasks`/`participants` array bodies via depth-aware scanning →
splits into element substrings → deserializes each independently, `LogWarning` + skip on a per-element
`JsonException` → returns `MeetingExtractionResult(salvagedTasks, salvagedParticipants, Degraded:
true)` → handler persists the salvaged tasks/participants exactly as it does today for the happy path,
plus `entity.TasksExtractionDegraded = true` → detail/list DTOs surface `true` → frontend banner/pill
render, pointing at "Reimport".

**Ingest (not JSON-shaped at all):** Same as above, but `TrySalvage` cannot even locate a `tasks` or
`participants` array → falls through to today's `MeetingExtractionResult([], [], Degraded: true)`
(still degraded — this is a behavior change from today's `Degraded`-less empty result, but the "return
empty tasks" behavior itself is unchanged, matching FR-2 acceptance criterion (b)).

**Reimport:** Identical extractor call inside `ReimportMeetingTranscriptHandler`; the only difference
from ingest is that `transcript.TasksExtractionDegraded` is **unconditionally overwritten** (not
OR'd) with the fresh `extraction.Degraded` on every reimport, per Decision 3 — so a clean reimport
clears a previously-set flag, and a still-degraded reimport keeps it set.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| The spec's literal FR-2 technique (`JsonDocument.Parse` "permissive mode") does not exist and cannot achieve element-level salvage on a document with an internal malformed byte — building it as literally specified would silently regress to "always full fallback, never partial salvage," defeating FR-2's purpose | High | Implement Decision 2's custom depth-aware raw-text scanner instead; call this out explicitly in the plan/PR description so the deviation from literal spec wording is visible and reviewable, not silent |
| Depth-aware string/bracket scanner is genuinely fiddly (escaped quotes, nested braces inside `description` text, truncated trailing element, unicode surrogate pairs in Czech names) — easy to get subtly wrong | Medium | Dedicated `PartialExtractionParserTests.cs` covering each primitive in isolation with adversarial fixtures (escaped quotes, brace-containing descriptions, truncated array, empty array, missing key) *before* wiring into the extractor, per TDD; the 3 real malformed payloads captured by FR-1's new logging after this ships should be added as regression fixtures once observed |
| Manual EF Core migration drifts from the model snapshot if hand-written (wrong column type/default vs. what `MeetingTranscriptConfiguration` declares) | Low | Prefer generating the migration via `dotnet ef migrations add AddTasksExtractionDegraded` against the updated `MeetingTranscriptConfiguration`/entity rather than hand-authoring, then eyeball against the `AddMeetingParticipants`/`AddInvoiceAcquiredToPurchaseOrder` precedents for `AddColumn<bool>` shape (`nullable: false, defaultValue: false`) |
| `useMeetingTasks.ts`'s hand-written DTO interface silently drifts from the real API shape over time (already true today — this PR doesn't fix that, only adds one more field to keep in sync by hand) | Low (pre-existing) | Add the field by hand as scoped here; do not expand scope to the client-generation migration noted in the file's own TODO — flag it as a separate follow-up if desired, not part of this fix |
| Logging full raw LLM response text (with Czech names/emails) at `LogError` on every failure, at ~23% of a background job's calls | Low (spec already accepts this, NFR-2) | No action beyond what NFR-2 already specifies — raw transcript content is already at rest in `MeetingTranscript.RawTranscript`; call out log-retention policy as an open question for the org if it differs from DB retention |

## Specification Amendments

1. **FR-2's acceptance-criteria wording naming `JsonDocument.Parse` in "permissive/best-effort mode"
   should be replaced** with the depth-aware raw-text scanning approach in Decision 2. As written, the
   named technique cannot parse past an internal malformed byte at all (it will throw at the identical
   position `JsonSerializer.Deserialize` did), so it cannot satisfy FR-2's own goal of salvaging
   sibling elements. This is a mechanism correction, not a scope change — the acceptance criteria
   themselves (skip-and-log per malformed element, preserve order, `Degraded` flag, three-tier
   fallback) are unchanged and are what the new mechanism is validated against.
2. **The Data Model section's claim that "the frontend `TranscriptDto`/equivalent type in
   `useMeetingTasks.ts` ... picks it up on the next generated-client build" is incorrect for this
   module** — `useMeetingTasks.ts` hand-rolls its own `MeetingTranscriptDto` interface and bypasses the
   generated client (`api-client.ts` already independently has a generated `MeetingTranscriptDto` with
   `meetingTasks_*` methods that the hook never calls). Add an explicit sub-task to FR-3: manually add
   `tasksExtractionDegraded: boolean` to the interface in `useMeetingTasks.ts`. Regenerating the client
   is still necessary (keeps `api-client.ts` itself correct/unused-but-consistent) but is not
   sufficient on its own.
3. No other amendments — the entity, DTO, and handler-level design in the spec matches the existing
   code shape exactly as written (verified against the real `MeetingTranscript.cs`,
   `MeetingTranscriptDto.cs`, both handlers, and both frontend pages).

## Prerequisites

- **Manual EF Core migration**, per project convention (migrations are applied manually, not part of
  deployment automation). Precedent to follow: `20260714103910_AddMeetingParticipants.cs` (single
  `AddColumn` on this same `MeetingTranscripts` table) and `20260901084258_AddInvoiceAcquiredToPurchaseOrder.cs`
  / other `AddColumn<bool>(..., nullable: false, defaultValue: false)` precedents for the exact bool
  shape. Add the column and `EntityTypeConfiguration.Property(...).IsRequired().HasDefaultValue(false)`
  together, generate via `dotnet ef migrations add AddTasksExtractionDegraded --project
  backend/src/Anela.Heblo.Persistence --startup-project backend/src/Anela.Heblo.API`, then apply it to
  the local/staging database by hand before the feature can be exercised end-to-end (existing rows
  default to `false`, matching the spec's explicit no-backfill decision).
- **OpenAPI client regeneration** (`npm run` client-gen step per `docs/development/api-client-generation.md`)
  must run after the backend `MeetingTranscriptDto` change, before the manual `useMeetingTasks.ts` edit
  is meaningful to type-check against (though the hook doesn't consume the generated type directly, the
  regenerated `api-client.ts` should still reflect the new field for consistency and any future
  consumer).
- No other infrastructure, config, or feature-flag prerequisites — `IChatClient`/Claude integration,
  Hangfire job wiring, and `IMeetingAccessGuard` are all unchanged and already in place.
