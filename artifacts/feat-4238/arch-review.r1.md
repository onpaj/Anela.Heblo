# Architecture Review: Persist an IsAllDay flag on MarketingAction

## Skip Design: true

Backend-only bug fix: a new persisted domain property, an EF migration, and corrected import/export mapping. No new or changed UI components, screens, or visual design decisions — the spec (NFR-2) explicitly keeps the UI/API surface unchanged. Designer phase can be skipped.

## Architectural Fit Assessment

This fits cleanly into the existing Marketing vertical slice. `MarketingAction` is a rich domain entity (private-set properties, mutation only through named domain methods: `Reschedule`, `UpdateDetails`, `MarkOutlookSynced`, etc.) sitting in `Anela.Heblo.Domain.Features.Marketing`, with EF configuration in `Anela.Heblo.Persistence.Marketing.MarketingActionConfiguration`, and Outlook sync logic split across two layers that already have a one-directional dependency the fix must respect:

- `Anela.Heblo.Application.Features.Marketing.Services.OutlookEventImportMapper` (Application layer) — builds/updates `MarketingAction` from an `OutlookEventDto` on import.
- `Anela.Heblo.Adapters.Microsoft365.OutlookCalendarSyncService` (Adapters layer) — builds the outbound Graph request body from a `MarketingAction` on export.

Both already reference `MarketingAction` directly; adding a new persisted property and reading/writing it from both sides is the same shape of change these two classes already make for `StartDate`/`EndDate`. No new module, no new cross-module dependency, no boundary violation. This is a same-pattern extension, not a new pattern.

## Proposed Architecture

### Component Overview

```
                     evt.IsAllDay (Graph, authoritative)
                              |
                              v
  OutlookEventImportMapper.BuildAction / ApplyChanges
      -> MarketingAction(..., isAllDay: evt.IsAllDay)
      -> existing.UpdateDetails(..., isAllDay: evt.IsAllDay)
      -> HasChanges(...) now also diffs IsAllDay
                              |
                              v
                  MarketingAction.IsAllDay (persisted, EF-mapped)
                              |
                              v
  OutlookCalendarSyncService.BuildEventBody
      -> isAllDay = action.IsAllDay          (was: IsDateOnly(action) guess)
      -> BuildGraphEnd(action, action.IsAllDay)
                              |
                              v
                     Graph isAllDay (outbound)

  Manual create/update (no Graph event involved):
  CreateMarketingActionHandler / UpdateMarketingActionHandler
      -> compute isAllDay via shared helper (same midnight-to-midnight rule
         IsDateOnly used) from request.StartDate/EndDate
      -> pass into constructor / UpdateDetails alongside dates
```

### Key Design Decisions

#### Decision 1: Where the manual-path "midnight-to-midnight" default lives

**Options considered:**
- (a) Duplicate the midnight-to-midnight check in each of `CreateMarketingActionHandler` and `UpdateMarketingActionHandler`.
- (b) Move the exact logic of the old `OutlookCalendarSyncService.IsDateOnly` into a small static helper in the Domain layer (e.g. `MarketingAction.IsMidnightToMidnight(DateTime start, DateTime? end)` or a free function in `Anela.Heblo.Domain.Features.Marketing`), called by both handlers.
- (c) Push the decision down into the `MarketingAction` constructor/`UpdateDetails` itself, i.e. make `isAllDay` an *optional* parameter that self-computes when not supplied.

**Chosen approach:** (b) — a single static helper, in the Domain layer next to `MarketingAction` (a private static method is not visible to the Application-layer handlers, so this must be a small `internal static` or `public static` helper — a good place is a static method on `MarketingAction` itself, e.g. `public static bool ComputeIsAllDay(DateTime startDate, DateTime? endDate)`, mirroring the shape of the deleted `IsDateOnly`).

**Rationale:** (a) duplicates business logic the spec explicitly calls out as needing to live in one place (FR-4 acceptance criteria). (c) is rejected because it hides a business rule behind an optional-parameter default — the *caller* (import vs. manual create) must make an explicit, deliberate choice between "trust Graph" and "guess from dates," and an implicit default blurs exactly the asymmetry this bug fix is closing. A static helper on the entity keeps the rule domain-owned (no logic leaks into the Application-layer handlers) while making both call sites pass an explicit value.

#### Decision 2: `Reschedule` does not take an `IsAllDay` parameter

**Options considered:**
- (a) As the spec suggested: add `bool isAllDay` to `Reschedule`'s signature, callers pass the action's own current value through unchanged.
- (b) Leave `Reschedule`'s signature unchanged; `Reschedule` simply does not touch `IsAllDay` at all, so it keeps whatever value the entity already had.

**Chosen approach:** (b).

**Rationale:** `Reschedule`'s only production caller is `MoveMarketingActionHandler` (the calendar drag-and-drop move), which today passes only `startDate`/`endDate`/actor/`utcNow` — it has no `IsAllDay` value to pass and no business reason to ever change it (a move changes *when* an action happens, not *what kind of* time range it spans). Adding a parameter that every caller would set to `action.IsAllDay` (its own current value) is pure ceremony with no behavioral benefit, and risks a future caller accidentally passing the wrong value. `IsAllDay` is already `private set`; simply not exposing it as a `Reschedule` parameter is sufficient to leave it untouched. This resolves spec Open Question 2: reschedule never changes all-day-ness, and no signature change is needed to guarantee that.

#### Decision 3: `Constructor` and `UpdateDetails` do take an explicit `isAllDay` parameter

**Options considered:** same shape as Decision 1 but scoped to these two methods specifically.

**Chosen approach:** Add `bool isAllDay` as a required parameter to `MarketingAction`'s constructor and to `UpdateDetails`, positioned next to `endDate` (both already group `startDate`/`endDate` together; `isAllDay` is inseparable from that pair per the spec's own reasoning in FR-1).

**Rationale:** Both of these are the two places where a `MarketingAction`'s full date/all-day identity is (re)established from an external source of truth (a create request or an update request, either manual or Outlook-originated). Both callers (handlers and `OutlookEventImportMapper`) already have everything they need to supply an explicit value once Decision 1's helper exists, so this is a compile-time-enforced guarantee that nobody can construct/update a `MarketingAction` without deciding its all-day-ness — exactly the class of bug this issue is fixing.

## Implementation Guidance

### Directory / Module Structure

No new files/directories beyond the migration. Changes land in:

- `backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs` — add `IsAllDay` property, extend constructor and `UpdateDetails`, add `ComputeIsAllDay` static helper (replaces `OutlookCalendarSyncService.IsDateOnly`, moved to the Domain layer since it's a pure date-shape rule, not an Outlook-specific one).
- `backend/src/Anela.Heblo.Persistence/Marketing/MarketingActionConfiguration.cs` — map `IsAllDay` (`builder.Property(x => x.IsAllDay).IsRequired();`).
- `backend/src/Anela.Heblo.Persistence/Migrations/` — new migration `AddIsAllDayToMarketingAction` (or similar timestamped name matching the existing `yyyyMMddHHmmss_Name` convention seen in `20260910185627_AddProductPriceChangeLog.cs`), with a data-backfill `Sql(...)` step in `Up` (see Prerequisites).
- `backend/src/Anela.Heblo.Application/Features/Marketing/Services/OutlookEventImportMapper.cs` — `BuildAction`, `ApplyChanges`, `HasChanges` read/compare `evt.IsAllDay`.
- `backend/src/Adapters/Anela.Heblo.Adapters.Microsoft365/OutlookCalendarSyncService.cs` — `BuildEventBody` reads `action.IsAllDay`; delete `IsDateOnly`.
- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/CreateMarketingAction/CreateMarketingActionHandler.cs` and `.../UpdateMarketingAction/UpdateMarketingActionHandler.cs` — call `MarketingAction.ComputeIsAllDay(request.StartDate, request.EndDate)` and pass the result through.
- No change to `MoveMarketingActionHandler.cs` (Decision 2).

### Interfaces and Contracts

```csharp
// MarketingAction.cs
public bool IsAllDay { get; private set; }

public MarketingAction(
    string title,
    string? description,
    MarketingActionType actionType,
    DateTime startDate,
    DateTime? endDate,
    bool isAllDay,                 // NEW — inserted after endDate
    string createdByUserId,
    string? createdByUsername,
    DateTime utcNow)

public void UpdateDetails(
    string title,
    string? description,
    MarketingActionType actionType,
    DateTime startDate,
    DateTime? endDate,
    bool isAllDay,                 // NEW — inserted after endDate
    string modifiedByUserId,
    string? modifiedByUsername,
    DateTime utcNow)

// Reschedule: UNCHANGED signature (Decision 2) — does not set IsAllDay.

public static bool ComputeIsAllDay(DateTime startDate, DateTime? endDate) =>
    endDate is not null
    && startDate.TimeOfDay == TimeSpan.Zero
    && endDate.Value.TimeOfDay == TimeSpan.Zero;
```

Call-site impact (every one of these must be updated — enumerated from a full-repo search, treat this list as exhaustive, not illustrative):

- `OutlookEventImportMapper.BuildAction` / `ApplyChanges` → pass `evt.IsAllDay`.
- `CreateMarketingActionHandler` → pass `MarketingAction.ComputeIsAllDay(request.StartDate, request.EndDate)`.
- `UpdateMarketingActionHandler` → same.
- Tests that construct `MarketingAction` directly or call `UpdateDetails` and will fail to compile without a new argument: `MarketingActionConstructorTests.cs`, `MarketingActionTestBuilder.cs` (shared test builder — update once, most other test files likely consume it), `MarketingActionRepositoryGetPagedTests.cs`, `MarketingActionRepositoryGetSyncedInWindowTests.cs`, `OutlookEventImportMapperTests.cs`, `MarketingActionUpdateDetailsTests.cs`. Confirm `MarketingActionTestBuilder` is updated first since several of the above likely go through it — check before assuming each needs an independent fix.

### Data Flow

1. **Import (scheduled sync job → `OutlookEventImportMapper`):** Graph event → `evt.IsAllDay` read once → flows into both the entity's `IsAllDay` and (unchanged) the existing `ParseEndDate` exclusive/inclusive conversion. `HasChanges` gains `IsAllDay` in its diff so a Graph-side toggle with unchanged dates is still detected as an update.
2. **Export (`OutlookCalendarSyncService.BuildEventBody`):** `action.IsAllDay` is read once, used directly as the outbound `isAllDay` and as `BuildGraphEnd`'s branch condition — no independent re-derivation, closing the asymmetry.
3. **Manual create/update:** handler computes `IsAllDay` once via `ComputeIsAllDay`, passes it into the same constructor/`UpdateDetails` entry points import uses — one code path, two different sources for the boolean, exactly mirroring how `startDate`/`endDate` already work today.
4. **Reschedule (drag/move):** `IsAllDay` is untouched, carried forward from whatever it already was.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| New required constructor/`UpdateDetails` parameter breaks every existing call site and test at compile time | Medium | Expected and enumerated above; this is a compile-time (not runtime) list — `dotnet build` will find every remaining site exhaustively before merge. |
| Backfill migration computes a wrong one-time value for existing rows with an active Outlook sync | Low | Backfill uses the exact legacy `IsDateOnly` rule (spec FR-6), so no existing row's *live* export behavior changes at migration time; any row whose backfilled guess is wrong self-corrects on its next import cycle (FR-6 rationale). |
| A future caller of `Reschedule` assumes it can also change all-day-ness and is surprised it can't | Low | Add a one-line XML-doc comment on `Reschedule` noting `IsAllDay` is intentionally untouched (mirrors the existing style of inline rationale comments already in this file, e.g. above `IsDateOnly`/`BuildGraphEnd` in the pre-fix code). |
| `HasChanges` regression: forgetting to add `IsAllDay` to the comparison silently reintroduces a (smaller) version of the same asymmetry bug for the all-day-toggle-only case | Medium | Explicit acceptance criterion already in spec FR-2; call out in code review checklist. |

## Specification Amendments

1. **FR-1 / Interfaces:** `Reschedule` does **not** gain an `isAllDay` parameter (Decision 2) — this narrows spec FR-1's "the constructor, `UpdateDetails`, and `Reschedule` domain methods all accept and set `IsAllDay`" to constructor + `UpdateDetails` only. `Reschedule` deliberately leaves `IsAllDay` untouched.
2. **FR-4 / Open Question 2:** resolved — `Reschedule`/`MoveMarketingActionHandler` need no change at all; this removes one of the two items the spec flagged as needing architect confirmation.
3. **FR-4 helper placement:** the shared midnight-to-midnight computation specified as "one place shared by both handlers" is concretely placed as `MarketingAction.ComputeIsAllDay(DateTime, DateTime?)`, a public static method on the domain entity (Decision 1), not a new standalone service/class.
4. **Migration naming:** follow the existing `yyyyMMddHHmmss_DescriptiveName` pattern (e.g. `20260921HHMMSS_AddIsAllDayToMarketingAction`); exact timestamp generated by `dotnet ef migrations add` at implementation time, not hand-picked.

Open Question 1 (missing `docs/integrations/microsoft-graph-calendar.md`) and Open Question 3 (whether to expose `IsAllDay` on `MarketingActionDto`) are **not** resolved here — both are product/documentation decisions outside architectural scope, not blocking implementation of FR-1 through FR-5. Planner should carry them forward as follow-up items, not implementation tasks for this issue.

## Prerequisites

- A new EF Core migration (`AddIsAllDayToMarketingAction` or similar) must be generated via `dotnet ef migrations add` against the `Anela.Heblo.Persistence` project, and must include a raw-SQL (or `migrationBuilder.Sql(...)`) backfill statement for existing rows using the `ComputeIsAllDay` rule (spec FR-6), run in the same `Up()` after the column is added with a temporary default, then the column tightened to non-nullable if a two-step add-then-backfill-then-constrain approach is needed for existing data (standard pattern for adding a NOT NULL column to a populated table in PostgreSQL — add nullable/default, backfill, then alter to NOT NULL, or add with `defaultValue: false` and immediately overwrite via the backfill `Sql` statement; either is acceptable, developer's choice, but the backfill statement itself is not optional).
- Per project rules (`CLAUDE.md`), this migration is **not** auto-applied in deployment — it must be run manually against each environment (dev, staging, production) after merge, and the person merging must be told this explicitly in the PR description.
- No other infrastructure, config, or feature-flag prerequisites.
