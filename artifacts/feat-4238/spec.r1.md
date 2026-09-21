# Specification: Persist an IsAllDay flag on MarketingAction

## Summary

The Outlook calendar sync for `MarketingAction` is asymmetric: import reads Microsoft Graph's authoritative `isAllDay` flag, but export re-derives all-day-ness on every write by guessing from whether `StartDate`/`EndDate` both fall on midnight (`OutlookCalendarSyncService.IsDateOnly`). Because a genuinely timed, midnight-to-midnight event satisfies that guess, a 24-hour timed Outlook event can silently mutate into a 3-day all-day event after one edit round-trip through Heblo. This spec adds a persisted `IsAllDay` boolean to `MarketingAction`, sets it from Graph's `isAllDay` on import, and reads it (instead of guessing) on export, closing the round-trip corruption.

## Background

`MarketingAction` currently models scheduling with only `StartDate` (required) and `EndDate` (nullable, inclusive). Whether an action is "all-day" is never stored — it is inferred independently at each boundary:

- **Import** (`OutlookEventImportMapper.ParseEndDate`): told directly — reads `OutlookEventDto.IsAllDay`, which Graph populates from its own `isAllDay` field, to decide whether to convert Graph's exclusive end date into Heblo's inclusive `EndDate`.
- **Export** (`OutlookCalendarSyncService.IsDateOnly`): guesses — treats an action as all-day if and only if `EndDate` is non-null and both `StartDate.TimeOfDay` and `EndDate.TimeOfDay` are zero.

These two rules are not inverses of each other, and Graph's own exclusive/inclusive end-date convention amplifies the mismatch:

1. A **timed** Outlook event `18.9. 00:00 → 19.9. 00:00` (`isAllDay: false`, a 24-hour meeting) imports through the timed branch (`ParseEndDate` returns `evt.EndUtc` verbatim) to `StartDate = 18.9. 00:00`, `EndDate = 19.9. 00:00`.
2. That stored row now satisfies `IsDateOnly` (both `TimeOfDay` are zero), even though Graph never called it all-day.
3. The next Heblo-side edit calls `UpdateEventAsync`, which recomputes `isAllDay = true` via the guess and pushes `end = EndDate.AddDays(1) = 20.9. 00:00` (the inclusive→exclusive all-day conversion).
4. **The 24-hour timed event is now a 3-day all-day event on the shared group calendar** — a silent, one-edit data corruption with no error or warning anywhere in the pipeline.

A related, currently-unreachable edge case shares the same root cause: `IsDateOnly` requires a non-null `EndDate`, so a midnight-start action with `EndDate == null` exports as a 1-hour timed meeting at midnight (`BuildGraphEnd` falls back to `StartDate.AddHours(1)`) even if the action was actually meant to represent an all-day item. This is not reachable from the current UI (the end field is required there), but the API/domain contract (`EndDate` is nullable) permits it, and the meaning of a null end is entangled with the same all-day decision this spec addresses.

The issue also references `docs/integrations/microsoft-graph-calendar.md` ("Known gaps") for further context. **That file does not exist in this repository** (checked working tree and full git history) — this is a documentation gap the issue assumes is already filled; it is not. This spec proceeds from the code and tests actually present (`OutlookCalendarSyncService.cs`, `OutlookEventImportMapper.cs`, `OutlookEventDto.cs`, and their test suites) and flags the missing doc in Open Questions rather than silently inventing its contents.

This fix was found during code review of #4224 and #4237 (both already merged, per the branch history), which hardened the export-side all-day date-boundary math but did not address this deeper asymmetry.

## Functional Requirements

### FR-1: Persist `IsAllDay` on `MarketingAction`

Add a required, non-nullable `bool IsAllDay` property to the `MarketingAction` domain entity, defaulting to `false` for any code path that does not explicitly set it.

**Acceptance criteria:**
- `MarketingAction` exposes `IsAllDay` as a `private set` (or equivalent), consistent with the entity's existing encapsulation style (`StartDate`, `EndDate`, etc. are `private set`, mutated only through domain methods).
- The constructor, `UpdateDetails`, and `Reschedule` domain methods all accept and set `IsAllDay` alongside `startDate`/`endDate`, since all three currently accept dates together and represent the entity's date/all-day-ness as one atomic concern.
- Existing rows (post-migration) have a deterministic, documented default — see FR-4.

### FR-2: Set `IsAllDay` from Graph on import

`OutlookEventImportMapper` must set the new `MarketingAction.IsAllDay` directly from `OutlookEventDto.IsAllDay` (Graph's own flag) for both the create path (`BuildAction`) and the update path (`ApplyChanges`), replacing any inference.

**Acceptance criteria:**
- `BuildAction` passes `evt.IsAllDay` into the `MarketingAction` constructor.
- `ApplyChanges` passes `evt.IsAllDay` into `UpdateDetails`.
- `HasChanges` includes `IsAllDay` in its change-detection comparison (currently compares `Title`, `Description`, `StartDate`, `EndDate`, `ActionType`) — an Outlook-side all-day toggle with unchanged dates must still be detected as a change and re-synced to Heblo's own record of it.
- `ParseEndDate`'s existing exclusive→inclusive end-date conversion logic is unchanged in behavior; it already consumes `evt.IsAllDay` correctly for that purpose.

### FR-3: Read `IsAllDay` on export (stop guessing)

`OutlookCalendarSyncService.BuildEventBody` must read `action.IsAllDay` directly instead of calling `IsDateOnly(action)`.

**Acceptance criteria:**
- `BuildEventBody` uses `action.IsAllDay` as the `isAllDay` value sent to Graph and as the input to `BuildGraphEnd`'s inclusive→exclusive conversion.
- The `IsDateOnly` heuristic method is removed (its only caller is replaced); if any other caller is found during implementation, that is a signal the removal needs re-scoping — flag it rather than leaving a second, now-inconsistent heuristic in place.
- `BuildGraphEnd`'s handling of `EndDate == null` (falls back to `StartDate.AddHours(1)`, always as a timed event) is preserved as-is: this spec does not change what a null `EndDate` means when `IsAllDay` is true, since the UI cannot currently produce that combination (see Open Questions).

### FR-4: Manually-created/edited actions (non-Outlook origin)

Actions created or edited directly in Heblo (`CreateMarketingActionHandler`, `UpdateMarketingActionHandler`) do not currently collect an all-day flag from the UI or API contract (`CreateMarketingActionRequest`, `UpdateMarketingActionRequest` carry only `StartDate`/`EndDate`). To avoid a behavior regression for these paths (today they export as all-day whenever both dates land on midnight, via the guess this spec removes), the handlers must compute `IsAllDay` using the **same midnight-to-midnight rule the old `IsDateOnly` heuristic used** (non-null `EndDate`, both `TimeOfDay == TimeSpan.Zero`) and pass that computed value into the constructor/`UpdateDetails` call, so manually-authored actions keep today's observable export behavior.

**Acceptance criteria:**
- `CreateMarketingActionHandler` and `UpdateMarketingActionHandler` compute `IsAllDay` from `request.StartDate`/`request.EndDate` using the midnight-to-midnight rule and pass it through.
- This computation lives in one place shared by both handlers (or on the request/mapping layer), not duplicated — mirroring how `IsDateOnly` was previously a single static method.
- A manually-created action with both dates at midnight still round-trips through export→import→export without its `IsAllDay` value flipping (this is the FR-4 regression-avoidance counterpart to the FR-5 round-trip test, which covers the Outlook-authored direction).
- This computed default is exactly that — a default for the current no-UI-toggle state — and does not preclude a future UI toggle; no UI change is made as part of this spec (see Non-Functional / Out of Scope).

### FR-5: Round-trip regression test

Add a test that walks the **import → export** direction (the direction with no coverage today, per the issue), reproducing the exact failure scenario from the issue.

**Acceptance criteria:**
- A test simulating import of a Graph event with `isAllDay: false`, `start = 18.9. 00:00`, `end = 19.9. 00:00` (a timed midnight-to-midnight event) results in a `MarketingAction` with `IsAllDay == false`.
- Exporting that same `MarketingAction` (`BuildEventBody`) produces `isAllDay: false` and `end = 19.9. 00:00` (unchanged from the original Graph event) — not `isAllDay: true` / `end = 20.9. 00:00`.
- The existing `CreateEventAsync_ThenImportingWhatWasSent_ReproducesTheOriginalDates` test (export → import direction) continues to pass unmodified in intent; it may need updating only for the new required constructor/method parameter, not for its assertions.
- A companion test confirms a genuine all-day import (`isAllDay: true`) still round-trips correctly (`IsAllDay == true` after import, `isAllDay: true` on re-export).

## Non-Functional Requirements

### NFR-1: Backward compatibility / migration

This is an additive schema change (`IsAllDay` is required at the domain level but needs a backfill strategy for existing rows). See FR-6 (Data Model) for the concrete backfill rule. Per project rules, the EF Core migration is written but **not** auto-applied — this repository's migrations are manual (`CLAUDE.md`: "Database migrations are manual (not automated in deployment)").

### NFR-2: No UI/API surface change required

This spec fixes an internal sync-correctness bug. It does not require adding an `IsAllDay` field to `MarketingActionDto`, `CreateMarketingActionRequest`, or `UpdateMarketingActionRequest`, and does not require a frontend change, since the manual-creation paths derive the value automatically (FR-4). Exposing `IsAllDay` as a user-facing toggle is explicitly out of scope (see Out of Scope) unless the architect/designer determine the DTO should carry it anyway for read-side transparency (see Open Questions).

## Data Model

### FR-6: `MarketingAction.IsAllDay`

| Property | Type | Nullable | Notes |
|---|---|---|---|
| `IsAllDay` | `bool` | No | New. Set by import (FR-2) from Graph's `isAllDay`, or computed by create/update handlers (FR-4) from the midnight-to-midnight rule. Read directly by export (FR-3). |

**Migration / backfill:** existing `MarketingActions` rows have no source of truth for this flag. The migration must backfill `IsAllDay` using the same midnight-to-midnight rule as FR-4 (`EndDate IS NOT NULL AND date_trunc('day', "StartDate") = "StartDate" AND date_trunc('day', "EndDate") = "EndDate"`, or the EF/CLR equivalent), i.e. **exactly reproducing today's `IsDateOnly` guess as the one-time backfill value** — this is the only backfill rule available since Graph's authoritative flag was never stored historically, and it guarantees the migration does not itself change any existing row's live Outlook sync behavior at the moment it runs. Rows with an active `OutlookEventId` will self-correct to the true Graph value on the next import cycle; this backfill only prevents a spurious change on the very next export before that import runs.

EF Core: add `builder.Property(x => x.IsAllDay).IsRequired();` to `MarketingActionConfiguration`. No index is needed — `IsAllDay` is not currently queried/filtered on.

## API / Interface Design

No public API (controller/MediatR contract) changes. Internal signature changes only:

- `MarketingAction` constructor: add `bool isAllDay` parameter.
- `MarketingAction.UpdateDetails(...)`: add `bool isAllDay` parameter.
- `MarketingAction.Reschedule(...)`: add `bool isAllDay` parameter (Reschedule currently takes `startDate`/`endDate` together; all-day-ness travels with them for the same reason described in FR-1).
- `OutlookEventImportMapper.BuildAction` / `ApplyChanges` / `HasChanges`: no signature change, but new internal behavior (FR-2).
- `OutlookCalendarSyncService.BuildEventBody`: no signature change; `IsDateOnly` is deleted (FR-3).

The architect/designer should confirm the exact call sites of `Reschedule` (e.g. drag-and-drop reschedule in the calendar UI) to decide what `IsAllDay` value those call sites should pass — likely "unchanged from the existing action" rather than recomputed, since a reschedule is not expected to change all-day-ness. This is flagged as an Open Question below rather than assumed.

## Dependencies

- EF Core migration tooling (manual apply per project convention).
- No new external dependencies. No Shoptet API involvement (out of module).
- Depends on `OutlookEventDto.IsAllDay` already existing and already being requested via `EventSelect` (`"id,subject,body,start,end,isAllDay,categories"`) — confirmed present, no Graph query change needed.

## Out of Scope

- Adding an `IsAllDay` toggle to the Heblo UI (calendar create/edit modal) or to `MarketingActionDto` / `CreateMarketingActionRequest` / `UpdateMarketingActionRequest`. Manually-created actions keep deriving the flag automatically per FR-4.
- Changing what a `null EndDate` combined with `IsAllDay == true` means end-to-end (the issue notes this combination is not reachable from the UI today; this spec preserves `BuildGraphEnd`'s existing fallback rather than redesigning it).
- Recreating or authoring `docs/integrations/microsoft-graph-calendar.md` — flagged in Open Questions, not undertaken here (no instruction to do so, and inventing doc content is out of scope for a bug-fix spec).
- Any change to the categories/subject/body mapping logic, token acquisition, or other unrelated parts of `OutlookCalendarSyncService`.

## Open Questions

1. **`docs/integrations/microsoft-graph-calendar.md` does not exist.** The issue references it for context ("Known gaps") but it is absent from the working tree and from git history. Should this spec's implementation also create that doc (documenting the `IsAllDay` fix per the project's Shoptet-style "document integration findings" convention), or is that tracked separately? **Assumption made for this spec: out of scope; flagged for the architect/product owner to confirm or create a follow-up.**
2. **`Reschedule` call sites**: does any caller of `MarketingAction.Reschedule` (e.g., a drag-to-reschedule UI action) need to pass a *changed* `IsAllDay`, or should it always pass through the action's current `IsAllDay` unchanged? **Assumption made for this spec: reschedule never changes all-day-ness; the architect should confirm by locating all call sites.**
3. Should `MarketingActionDto` expose `IsAllDay` for read-side transparency (e.g., so the frontend calendar could eventually render all-day items differently) even though no write path is being added? **Assumption made for this spec: no, per NFR-2 — can be added later as a separate, additive change with no migration impact.**

## Status: COMPLETE
